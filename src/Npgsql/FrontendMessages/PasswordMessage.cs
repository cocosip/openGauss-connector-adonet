#region License
// The PostgreSQL License
//
// Copyright (C) 2017 The Npgsql Development Team
//
// Permission to use, copy, modify, and distribute this software and its
// documentation for any purpose, without fee, and without a written
// agreement is hereby granted, provided that the above copyright notice
// and this paragraph and the following two paragraphs appear in all copies.
//
// IN NO EVENT SHALL THE NPGSQL DEVELOPMENT TEAM BE LIABLE TO ANY PARTY
// FOR DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES,
// INCLUDING LOST PROFITS, ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS
// DOCUMENTATION, EVEN IF THE NPGSQL DEVELOPMENT TEAM HAS BEEN ADVISED OF
// THE POSSIBILITY OF SUCH DAMAGE.
//
// THE NPGSQL DEVELOPMENT TEAM SPECIFICALLY DISCLAIMS ANY WARRANTIES,
// INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY
// AND FITNESS FOR A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED HEREUNDER IS
// ON AN "AS IS" BASIS, AND THE NPGSQL DEVELOPMENT TEAM HAS NO OBLIGATIONS
// TO PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
#endregion

using System;
using System.Security.Cryptography;
using System.Text;

namespace Npgsql.FrontendMessages
{
    class PasswordResponseMessage : SimpleFrontendMessage
    {
        const byte Code = (byte)'p';

        const string ClientKey = "Client Key";
        const string ServerKey = "Server Key";

        internal byte[] Result { get; }

        internal PasswordResponseMessage(string password, string random64Code, string token, int serverIteration)
        {
            Result = RFC5802Algorithm(password, random64Code, token, serverIteration);
        }

        internal override int Length => 1 + 4 + Result.Length + 1;

        internal override void WriteFully(WriteBuffer buf)
        {
            buf.WriteByte(Code);
            buf.WriteInt32(Length - 1);
            buf.WriteBytes(Result);
            buf.WriteByte(0);
        }

        static byte[] RFC5802Algorithm(string password, string random64code, string token, int server_iteration)
        {
            var K = GenerateKFromPBKDF2(password, random64code, server_iteration);

            var server_key = GetKeyFromHmac(K, Encoding.UTF8.GetBytes(ServerKey));
            var client_key = GetKeyFromHmac(K, Encoding.UTF8.GetBytes(ClientKey));

            byte[] stored_key;
            using (var sha256 = SHA256.Create())
                stored_key = sha256.ComputeHash(client_key);

            var tokenbyte = HexStringToBytes(token);

            //byte[] client_signature = GetKeyFromHmac(server_key, tokenbyte);

            //if (server_signature != null && server_signature != BytesToHexString(client_signature))
            //{
            //    return new byte[0];
            //}

            var hmac_result = GetKeyFromHmac(stored_key, tokenbyte);
            var h = XOR_between_password(hmac_result, client_key, client_key.Length);

            var result = new byte[h.Length * 2];
            BytesToHex(h, result, 0, h.Length);

            return result;
        }

        static byte[] GenerateKFromPBKDF2(string password, string random64code, int server_iteration)
        {
            var chars = Encoding.UTF8.GetBytes(password);
            var random32code = HexStringToBytes(random64code);

            using (var pbkdf2 = new Rfc2898DeriveBytes(chars, random32code, server_iteration)) 
            {
                return pbkdf2.GetBytes(32);
            }
        }

        static byte[] HexStringToBytes(string hex)
        {
            var NumberChars = hex.Length;
            var bytes = new byte[NumberChars / 2];
            for (var i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        static string BytesToHexString(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "");
        }

        static void BytesToHex(byte[] bytes, byte[] hex, int offset, int length)
        {
            var lookup = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
            var pos = offset;

            for (var i = 0; i < length; ++i)
            {
                var c = bytes[i] & 255;
                var j = c >> 4;
                hex[pos++] = (byte)lookup[j];
                j = c & 15;
                hex[pos++] = (byte)lookup[j];
            }
        }

        static byte[] XOR_between_password(byte[] password1, byte[] password2, int length)
        {
            var temp = new byte[length];

            for (var i = 0; i < length; ++i)
            {
                temp[i] = (byte)(password1[i] ^ password2[i]);
            }

            return temp;
        }

        static byte[] GetKeyFromHmac(byte[] key, byte[] data)
        {
            using (var hmacsha256 = new HMACSHA256(key)) 
            {
                return hmacsha256.ComputeHash(data);
            }
        }
    }
}
