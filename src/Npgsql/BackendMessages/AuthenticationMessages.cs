using System.Collections.Generic;
using Npgsql.Logging;
using Npgsql.Util;

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

        internal AuthenticationPasswordMessage(NpgsqlReadBuffer buf)
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
