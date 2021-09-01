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

namespace Npgsql.BackendMessages
{
    abstract class AuthenticationRequestMessage : IBackendMessage
    {
        public BackendMessageCode Code => BackendMessageCode.AuthenticationRequest;
        internal abstract AuthenticationRequestType AuthRequestType { get; }
    }

    class AuthenticationOkMessage : AuthenticationRequestMessage
    {
        internal override AuthenticationRequestType AuthRequestType => AuthenticationRequestType.AuthenticationOk;

        internal static readonly AuthenticationOkMessage Instance = new AuthenticationOkMessage();
        AuthenticationOkMessage() { }
    }

    class AuthenticationPasswordMessage : AuthenticationRequestMessage
    {
        internal override AuthenticationRequestType AuthRequestType => AuthenticationRequestType.AuthenticationPassword;

        internal int StoredMethod { get; private set; }

        internal string Random64Code { get; private set; }

        internal string Token { get; private set; }

        internal int ServerIteration { get; private set; }

        internal AuthenticationPasswordMessage(ReadBuffer buf)
        {
            StoredMethod = buf.ReadInt32();

            if (StoredMethod != 2)
            {
                throw new NpgsqlException("The  password-stored method is not supported , must sha256.");
            }

            Random64Code = buf.ReadString(64);
            Token = buf.ReadString(8);
            ServerIteration = buf.ReadInt32();
        }
    }

    // TODO: Remove Authentication prefix from everything
    enum AuthenticationRequestType
    {
        AuthenticationOk = 0,
        AuthenticationPassword = 10,
    }
}
