using JetBrains.Annotations;
using Npgsql.BackendMessages;
using Npgsql.FrontendMessages;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Npgsql
{
    partial class NpgsqlConnector
    {
        async Task Authenticate(string username, NpgsqlTimeout timeout, bool async, CancellationToken cancellationToken)
        {
            Log.Trace("Authenticating...", Id);

            var msg = await ReadExpecting<AuthenticationRequestMessage>(async);
            timeout.Check();
            switch (msg.AuthRequestType)
            {
                case AuthenticationRequestType.AuthenticationOk:
                    return;

                case AuthenticationRequestType.AuthenticationPassword:
                    var _msg = (AuthenticationPasswordMessage)msg;
                    await AuthenticatePassword(_msg.Random64Code, _msg.Token, _msg.ServerIteration, async, cancellationToken);
                    return;

                default:
                    throw new NotSupportedException($"Authentication method not supported (Received: {msg.AuthRequestType})");
            }
        }

        async Task AuthenticatePassword(string random64Code, string token, int serverIteration, bool async, CancellationToken cancellationToken = default)
        {
            var password = GetPassword();
            if (password == null)
                throw new NpgsqlException("No password has been provided but the backend requires one (in cleartext)");

            var passwordResponseMessage = new PasswordResponseMessage(password, random64Code, token, serverIteration);
            await passwordResponseMessage.Write(WriteBuffer, async, cancellationToken);

            var okMsg = await ReadExpecting<AuthenticationRequestMessage>(async);
            if (okMsg.AuthRequestType != AuthenticationRequestType.AuthenticationOk)
                throw new NpgsqlException("[SASL] Expected AuthenticationOK message");
        }

        [CanBeNull]
        string GetPassword()
        {
            var passwd = Settings.Password;
            if (passwd != null)
                return passwd;

            // No password was provided. Attempt to pull the password from the pgpass file.
            var matchingEntry = PgPassFile.LoadDefaultFile()?.GetFirstMatchingEntry(Settings.Host, Settings.Port, Settings.Database, Settings.Username);
            if (matchingEntry != null)
            {
                Log.Trace("Taking password from pgpass file");
                return matchingEntry.Password;
            }

            return null;
        }
    }
}
