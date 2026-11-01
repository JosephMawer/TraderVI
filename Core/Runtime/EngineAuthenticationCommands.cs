#nullable enable
using Core.WSTrade.Authentication;
using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core.Runtime;

// Shared by the normal engine and the sign-in-only entry point. The latter has no order writer.
internal static class EngineAuthenticationCommands
{
    internal static bool Accepts(EngineOperation operation) => operation is EngineOperation.SignIn or
        EngineOperation.SubmitOtp or EngineOperation.CancelSignIn or EngineOperation.ConnectProtectedSession or
        EngineOperation.CheckConnection or EngineOperation.RememberSession or EngineOperation.Disconnect;

    internal static async Task<bool> DispatchAsync(EngineRequest request, IWealthsimpleConnection connection,
        IWealthsimpleSessionProtector protector)
    {
        T Read<T>() => request.Payload.Deserialize<T>() ?? throw new InvalidOperationException("Missing sign-in input.");
        switch (request.Operation)
        {
            case EngineOperation.SignIn:
                var login = Read<EngineSignIn>();
                await connection.SignInAsync(login.Email, login.Password, login.Remember);
                break;
            case EngineOperation.SubmitOtp: await connection.SubmitOtpAsync(Read<string>()); break;
            case EngineOperation.CancelSignIn: await connection.CancelSignInAsync(); break;
            case EngineOperation.ConnectProtectedSession:
                var transfer = Read<EngineProtectedSession>();
                try { await connection.ConnectAsync(WealthsimpleAuthClient.FromProtectedSession(transfer.ProtectedSession, protector), transfer.Remember); }
                finally { CryptographicOperations.ZeroMemory(transfer.ProtectedSession); }
                break;
            case EngineOperation.CheckConnection: await connection.CheckAsync(true); break;
            case EngineOperation.RememberSession: await connection.SetRememberAsync(Read<bool>()); break;
            case EngineOperation.Disconnect: await connection.DisconnectAsync(); break;
            default: throw new InvalidOperationException("Sign-in mode cannot perform this operation.");
        }
        return true;
    }
}
