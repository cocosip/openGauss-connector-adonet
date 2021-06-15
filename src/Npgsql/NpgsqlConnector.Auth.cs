using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql.BackendMessages;
using Npgsql.Util;
using static Npgsql.Util.Statics;

namespace Npgsql
{
    partial class NpgsqlConnector
    {
        async Task Authenticate(string username, NpgsqlTimeout timeout, bool async, CancellationToken cancellationToken)
        {
            Log.Trace("Authenticating...", Id);

            timeout.CheckAndApply(this);
            var msg = Expect<AuthenticationRequestMessage>(await ReadMessage(async), this);
            switch (msg.AuthRequestType)
            {
            case AuthenticationRequestType.AuthenticationOk:
                return;

            case AuthenticationRequestType.AuthenticationPassword:
                await AuthenticatePassword(username, (AuthenticationPasswordMessage)msg, async, cancellationToken);
                return;
            default:
                throw new NotSupportedException($"Authentication method not supported (Received: {msg.AuthRequestType})");
            }
        }

        async Task AuthenticatePassword(string username, AuthenticationPasswordMessage msg, bool async, CancellationToken cancellationToken = default)
        {
            var password = GetPassword(username);
            if (password == null)
                throw new NpgsqlException("No password has been provided but the backend requires one (in cleartext)");

            var result = RFC5802Algorithm(password, msg.Random64Code, msg.Token, msg.ServerIteration);

            await WritePassword(result, async, cancellationToken);

            var okMsg = Expect<AuthenticationRequestMessage>(await ReadMessage(async), this);
            if (okMsg.AuthRequestType != AuthenticationRequestType.AuthenticationOk)
                throw new NpgsqlException("Expected AuthenticationOK message");
        }

        string? GetPassword(string username)
        {
            var password = Settings.Password;
            if (password != null)
                return password;

            if (ProvidePasswordCallback is { } passwordCallback)
                try
                {
                    Log.Trace($"Taking password from {nameof(ProvidePasswordCallback)} delegate");
                    password = passwordCallback(Host, Port, Settings.Database!, username);
                }
                catch (Exception e)
                {
                    throw new NpgsqlException($"Obtaining password using {nameof(NpgsqlConnection)}.{nameof(ProvidePasswordCallback)} delegate failed", e);
                }

            if (password is null)
                password = PostgresEnvironment.Password;

            if (password != null)
                return password;

            var passFile = Settings.Passfile ?? PostgresEnvironment.PassFile ?? PostgresEnvironment.PassFileDefault;
            if (passFile != null)
            {
                var matchingEntry = new PgPassFile(passFile!)
                    .GetFirstMatchingEntry(Host, Port, Settings.Database!, username);
                if (matchingEntry != null)
                {
                    Log.Trace("Taking password from pgpass file");
                    password = matchingEntry.Password;
                }
            }

            return password;
        }

        static byte[] RFC5802Algorithm(string password, string random64code, string token, int server_iteration)
        {
            var K = GenerateKFromPBKDF2(password, random64code, server_iteration);

            var server_key = GetKeyFromHmac(K, Encoding.UTF8.GetBytes("Sever Key"));
            var client_key = GetKeyFromHmac(K, Encoding.UTF8.GetBytes("Client Key"));

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

            using var pbkdf2 = new Rfc2898DeriveBytes(chars, random32code, server_iteration);
            return pbkdf2.GetBytes(32);
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
            using var hmacsha256 = new HMACSHA256(key);
            return hmacsha256.ComputeHash(data);
        }

    }
}
