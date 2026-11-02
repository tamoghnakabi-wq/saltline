using System;
using System.Text;
using System.Threading.Tasks;
using Saltline.Core;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Saltline.Net
{
    /// <summary>
    /// Initializes Unity Gaming Services and signs the player in anonymously.
    /// Each process can use its own authentication profile (-profile name), which is what
    /// lets several game instances on one machine appear as different players.
    /// </summary>
    public static class UgsAuth
    {
        static Task s_InitTask;

        public static bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;

        public static string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : "";

        public static string Profile { get; private set; } = "default";

        public static Task EnsureSignedInAsync()
        {
            if (s_InitTask == null || s_InitTask.IsFaulted || s_InitTask.IsCanceled)
                s_InitTask = InitializeAndSignInAsync();
            return s_InitTask;
        }

        static async Task InitializeAndSignInAsync()
        {
            Profile = SanitizeProfile(CommandLine.Get("profile", "default"));

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                options.SetProfile(Profile);
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            Debug.Log($"[SALTLINE] UGS signed in. profile={Profile} playerId={AuthenticationService.Instance.PlayerId}");
        }

        /// <summary>Profiles allow only letters, digits, '-' and '_' and at most 30 characters.</summary>
        public static string SanitizeProfile(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "default";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            string s = sb.ToString();
            if (s.Length == 0) return "default";
            return s.Length > 30 ? s.Substring(0, 30) : s;
        }

        public static string Describe(Exception e)
        {
            switch (e)
            {
                case AuthenticationException ae:
                    return $"Sign-in failed ({ae.ErrorCode}). Check your internet connection and try again.";
                case RequestFailedException rf:
                    return $"Unity services request failed ({rf.ErrorCode}). Check your internet connection.";
                case ServicesInitializationException:
                    return "Unity services could not start. The build may be missing its cloud project link.";
                default:
                    return e?.Message ?? "Unknown error";
            }
        }
    }
}
