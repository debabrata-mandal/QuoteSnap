using System.Net;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteSnap.Client;
using QuoteSnap.Contracts;
using QuoteSnap.Desktop.Services;
using QuoteSnap.Desktop.Settings;
using QuoteSnap.Desktop.Text;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>
/// The Fyers login page: the session stored in the service, and the login the quotesnap-login tool does, run in the app.
/// The browser login opens the Fyers page and receives its redirect on the local callback address; the manual login
/// has you paste the redirect URL instead.
/// </summary>
public sealed partial class FyersLoginViewModel : ObservableObject
{
    private static readonly TimeSpan LoginExpiryWarning = TimeSpan.FromDays(2);
    private static readonly TimeSpan LoginLifetime = TimeSpan.FromDays(15);

    private readonly QuoteSnapConnection _connection;
    private readonly SettingsStore _store;
    private CancellationTokenSource? _login;
    private TaskCompletionSource<string>? _paste;
    private FyersSessionInfo? _shownSession;

    public FyersLoginViewModel(QuoteSnapConnection connection, SettingsStore store)
    {
        _connection = connection;
        _store = store;
        ShowDetails(store.LoadFyersLogin());
        connection.Changed += async (_, _) =>
        {
            OnPropertyChanged(nameof(HasAdminKey));
            await RefreshSessionAsync();
        };
    }

    /// <summary>Raised after a login is stored or forgotten, so other pages can refresh.</summary>
    public event EventHandler? SessionChanged;

    /// <summary>Raised when a login that sent you to the browser ends, however it ended, so the app can come back to the front.</summary>
    public event EventHandler? BrowserLoginEnded;

    /// <summary>Raised to open the Settings page, where the admin key is set.</summary>
    public event EventHandler? SettingsRequested;

    [ObservableProperty]
    public partial StatusLevel SessionLevel { get; set; }

    [ObservableProperty]
    public partial string SessionSummary { get; set; } = "Checking";

    [ObservableProperty]
    public partial string Account { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string DaysLeftText { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string LoginValidText { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string TokenText { get; set; } = Display.Missing;

    [ObservableProperty]
    public partial string UpdatedText { get; set; } = Display.Missing;

    /// <summary>From 1 just after a login down to 0 when it ends, about 15 days later.</summary>
    [ObservableProperty]
    public partial double Validity { get; set; }

    [ObservableProperty]
    public partial string? SessionError { get; set; }

    /// <summary>Whether the service has a login stored, which is what Log out removes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskLogOutCommand))]
    public partial bool HasStoredLogin { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingLogout { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskLogOutCommand), nameof(LogOutCommand), nameof(KeepLoginCommand), nameof(LogInCommand), nameof(LogInManuallyCommand))]
    public partial bool IsLoggingOut { get; set; }

    [ObservableProperty]
    public partial string? SessionMessageTitle { get; set; }

    [ObservableProperty]
    public partial string? SessionMessage { get; set; }

    [ObservableProperty]
    public partial StatusLevel SessionMessageLevel { get; set; }

    [ObservableProperty]
    public partial string AppId { get; set; } = "";

    [ObservableProperty]
    public partial string SecretId { get; set; } = "";

    [ObservableProperty]
    public partial string Pin { get; set; } = "";

    [ObservableProperty]
    public partial string RedirectUri { get; set; } = FyersLoginOptions.DefaultRedirectUri;

    /// <summary>Whether to keep the details on this PC after the next successful login.</summary>
    [ObservableProperty]
    public partial bool Remember { get; set; }

    [ObservableProperty]
    public partial bool IsRemembered { get; set; }

    [ObservableProperty]
    public partial string DetailsSource { get; set; } = "";

    public bool HasAdminKey => _connection.Settings.AdminApiKey is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDetails))]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand), nameof(LogInManuallyCommand), nameof(CancelCommand), nameof(AskLogOutCommand))]
    public partial bool IsLoggingIn { get; set; }

    public bool CanEditDetails => !IsLoggingIn;

    /// <summary>What the login is doing now, such as waiting for the browser.</summary>
    [ObservableProperty]
    public partial string? Step { get; set; }

    /// <summary>The Fyers login page of the current attempt, to open again or copy.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoginUrl))]
    public partial string? LoginUrl { get; set; }

    public bool HasLoginUrl => LoginUrl is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitPastedCommand))]
    public partial bool IsAwaitingPaste { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitPastedCommand))]
    public partial string PastedUrl { get; set; } = "";

    [ObservableProperty]
    public partial string? MessageTitle { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial StatusLevel MessageLevel { get; set; }

    /// <summary>Called when the page is opened.</summary>
    public Task ActivateAsync() => IsLoggingIn ? Task.CompletedTask : RefreshSessionAsync();

    [RelayCommand]
    private async Task RefreshSessionAsync()
    {
        try
        {
            ServiceStatus status = await _connection.Client.GetStatusAsync();
            ShowSession(status.Fyers, status.ServerTime);
            SessionError = null;
        }
        catch (Exception ex)
        {
            SessionLevel = StatusLevel.Unknown;
            SessionSummary = "Unknown";
            SessionError = ErrorText.Describe(ex, _connection.Settings.BaseAddress);
        }
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private bool CanLogIn() => !IsLoggingIn && !IsLoggingOut;

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private Task LogInAsync() => RunLoginAsync(
        "Opening the Fyers login page…",
        (flow, app, redirectUri, cancellationToken) => flow.LoginWithBrowserAsync(
            app,
            new FyersLoginOptions
            {
                RedirectUri = redirectUri,
                OpenBrowser = url =>
                {
                    LoginUrl = url;
                    FyersLoginOptions.DefaultOpenBrowser(url);
                    Step = "Log in to Fyers in your browser. This page continues by itself once Fyers sends you back (within 5 minutes).";
                },
            },
            cancellationToken));

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private Task LogInManuallyAsync() => RunLoginAsync(
        "Getting the Fyers login page…",
        (flow, app, redirectUri, cancellationToken) => flow.LoginManualAsync(app, WaitForPasteAsync, redirectUri, cancellationToken));

    private bool CanCancel() => IsLoggingIn;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _login?.Cancel();

    private bool CanSubmitPasted() => IsAwaitingPaste && !string.IsNullOrWhiteSpace(PastedUrl);

    [RelayCommand(CanExecute = nameof(CanSubmitPasted))]
    private void SubmitPasted()
    {
        IsAwaitingPaste = false;
        Step = "Storing the login in QuoteSnap…";
        _paste?.TrySetResult(PastedUrl);
    }

    [RelayCommand]
    private void OpenLoginPage()
    {
        if (LoginUrl is { } url)
            FyersLoginOptions.DefaultOpenBrowser(url);
    }

    [RelayCommand]
    private void CopyLoginUrl()
    {
        if (LoginUrl is { } url)
            Clipboard.SetText(url);
    }

    [RelayCommand]
    private void Forget()
    {
        _store.ForgetFyersLogin();
        ShowDetails(_store.LoadFyersLogin());
        Report(StatusLevel.Good, "Forgot the Fyers details saved on this PC.");
    }

    private bool CanAskLogOut() => HasStoredLogin && !IsLoggingIn && !IsLoggingOut;

    [RelayCommand(CanExecute = nameof(CanAskLogOut))]
    private void AskLogOut()
    {
        SessionMessage = null;
        IsConfirmingLogout = true;
    }

    private bool IsNotLoggingOut() => !IsLoggingOut;

    [RelayCommand(CanExecute = nameof(IsNotLoggingOut))]
    private void KeepLogin() => IsConfirmingLogout = false;

    [RelayCommand(CanExecute = nameof(IsNotLoggingOut))]
    private async Task LogOutAsync()
    {
        if (!HasAdminKey)
        {
            IsConfirmingLogout = false;
            ReportSession(StatusLevel.Bad, "Logging out needs the admin API key. Set Auth__AdminApiKey in the repository's .env, or enter it in Settings.");
            return;
        }

        DateTimeOffset? tokenExpires = _shownSession?.AccessTokenExpiresAt;
        IsLoggingOut = true;
        try
        {
            FyersLogoutResult result = await _connection.Client.LogoutFyersAsync();
            if (result.RevokedAtFyers)
                ReportSession(
                    StatusLevel.Good,
                    "QuoteSnap forgot the login, including the app details and PIN it stored, and Fyers no longer accepts its token. Quote requests are refused until you log in again.",
                    result.FyersId is { } id ? $"Logged out {id}" : "Already logged out");
            else
                ReportSession(
                    StatusLevel.Warning,
                    $"QuoteSnap forgot the login, so nothing can use or renew it, but Fyers couldn't be asked to revoke the token ({result.FyersError}). It stops working by itself {Display.WhenInSentence(tokenExpires)}.",
                    $"Logged out{(result.FyersId is { } account ? $" {account}" : "")}, Fyers not reached");
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (QuoteSnapApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            ReportSession(StatusLevel.Bad, "QuoteSnap rejected the admin key. It must match Auth__AdminApiKey in the API's .env; check it in Settings.");
        }
        catch (Exception ex)
        {
            ReportSession(StatusLevel.Bad, $"The logout failed, so you're still logged in. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
        finally
        {
            IsLoggingOut = false;
            IsConfirmingLogout = false;
        }

        await RefreshSessionAsync();
    }

    private async Task RunLoginAsync(
        string firstStep,
        Func<FyersLoginFlow, FyersAppCredentials, string, CancellationToken, Task<FyersSessionInfo>> login)
    {
        Message = null;
        MessageTitle = null;
        SessionMessage = null;
        IsConfirmingLogout = false;
        if (ReadCredentials() is not { } app)
            return;

        string redirectUri = RedirectUri.Trim();
        using CancellationTokenSource cancellation = new();
        _login = cancellation;
        IsLoggingIn = true;
        Step = firstStep;
        try
        {
            FyersSessionInfo session = await login(new FyersLoginFlow(_connection.Client), app, redirectUri, cancellation.Token);
            KeepDetails(new FyersLoginDetails(app.AppId, app.SecretId, app.Pin, redirectUri));
            ShowSession(session, DateTimeOffset.UtcNow);
            SessionError = null;
            Report(
                StatusLevel.Good,
                $"Stored in QuoteSnap {Display.WhenInSentence(session.UpdatedAt ?? DateTimeOffset.Now)}. Quote requests work again; QuoteSnap renews the token daily, and the login lasts until {Display.Time(session.RefreshTokenExpiresAt)}.",
                $"Logged in{(session.FyersId is { } id ? $" as {id}" : "")}");
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Report(StatusLevel.Warning, "Login cancelled. Nothing was changed.");
        }
        catch (FyersLoginException ex)
        {
            Report(StatusLevel.Bad, ex.Message);
        }
        catch (QuoteSnapApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Report(StatusLevel.Bad, "QuoteSnap rejected the admin key. It must match Auth__AdminApiKey in the API's .env; check it in Settings.");
        }
        catch (Exception ex)
        {
            Report(StatusLevel.Bad, $"The login failed. {ErrorText.Describe(ex, _connection.Settings.BaseAddress)}");
        }
        finally
        {
            bool openedBrowser = LoginUrl is not null;
            _login = null;
            _paste = null;
            IsAwaitingPaste = false;
            PastedUrl = "";
            LoginUrl = null;
            Step = null;
            IsLoggingIn = false;
            if (openedBrowser)
                BrowserLoginEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<string> WaitForPasteAsync(string loginUrl, CancellationToken cancellationToken)
    {
        LoginUrl = loginUrl;
        FyersLoginOptions.DefaultOpenBrowser(loginUrl);
        Step = "Log in to Fyers in your browser. When it shows the redirect page, copy the whole address from the address bar and paste it below.";
        _paste = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsAwaitingPaste = true;
        await using CancellationTokenRegistration registration = cancellationToken.Register(() => _paste?.TrySetCanceled(cancellationToken));
        return await _paste.Task;
    }

    private FyersAppCredentials? ReadCredentials()
    {
        if (!HasAdminKey)
        {
            Report(StatusLevel.Bad, "Logging in needs the admin API key. Set Auth__AdminApiKey in the repository's .env, or enter it in Settings.");
            return null;
        }

        string appId = AppId.Trim();
        string secretId = SecretId.Trim();
        string pin = Pin.Trim();
        List<string> missing = [];
        if (appId.Length == 0)
            missing.Add("App ID");
        if (secretId.Length == 0)
            missing.Add("Secret ID");
        if (pin.Length == 0)
            missing.Add("PIN");
        if (missing.Count > 0)
        {
            Report(StatusLevel.Bad, $"Enter the Fyers {string.Join(", ", missing)}.");
            return null;
        }

        if (!Uri.TryCreate(RedirectUri.Trim(), UriKind.Absolute, out _))
        {
            Report(StatusLevel.Bad, $"The redirect URI must be the full address registered on the Fyers app, e.g. {FyersLoginOptions.DefaultRedirectUri}.");
            return null;
        }

        return new FyersAppCredentials(appId, secretId, pin);
    }

    private void KeepDetails(FyersLoginDetails details)
    {
        if (Remember)
            _store.SaveFyersLogin(details);
        else if (IsRemembered)
            _store.ForgetFyersLogin();
        else
            return;
        ShowDetails(_store.LoadFyersLogin());
    }

    private void ShowDetails(LoadedFyersLogin loaded)
    {
        AppId = loaded.Details.AppId ?? "";
        SecretId = loaded.Details.SecretId ?? "";
        Pin = loaded.Details.Pin ?? "";
        RedirectUri = loaded.Details.RedirectUri;
        IsRemembered = loaded.IsRemembered;
        Remember = loaded.IsRemembered;
        DetailsSource = loaded.Source;
    }

    private void ShowSession(FyersSessionInfo session, DateTimeOffset now)
    {
        _shownSession = session;
        HasStoredLogin = session.FyersId is not null;
        TimeSpan? left = session.RefreshTokenExpiresAt - now;
        bool loggedIn = session.State is FyersAuthState.Ok or FyersAuthState.AccessTokenExpired;
        Validity = loggedIn && left is { } l ? Math.Clamp(l / LoginLifetime, 0, 1) : 0;
        Account = session.FyersId ?? "No login stored";
        UpdatedText = session.UpdatedAt is { } updated ? Display.When(updated) : Display.Missing;
        LoginValidText = session.RefreshTokenExpiresAt is { } ends ? $"Until {Display.Time(ends)}" : Display.Missing;
        DaysLeftText = !HasStoredLogin ? "Not logged in"
            : !loggedIn ? "Expired"
            : left is { TotalDays: >= 1 } days ? $"{Display.Count((int)days.TotalDays, "day")} left"
            : left is { TotalHours: > 0 } hours ? $"{Display.Count((int)Math.Ceiling(hours.TotalHours), "hour")} left"
            : Display.Missing;
        TokenText = session.State switch
        {
            FyersAuthState.Ok => $"Renews {Display.WhenInSentence(session.AccessTokenExpiresAt)}",
            FyersAuthState.AccessTokenExpired => "Renews on the next quote request",
            _ => Display.Missing,
        };

        if (!loggedIn)
        {
            SessionLevel = StatusLevel.Bad;
            SessionSummary = "Login needed";
        }
        else if (left < LoginExpiryWarning)
        {
            SessionLevel = StatusLevel.Warning;
            SessionSummary = "Expires soon";
        }
        else
        {
            SessionLevel = StatusLevel.Good;
            SessionSummary = "Logged in";
        }
    }

    private void Report(StatusLevel level, string message, string? title = null)
    {
        MessageLevel = level;
        MessageTitle = title;
        Message = message;
    }

    private void ReportSession(StatusLevel level, string message, string? title = null)
    {
        SessionMessageLevel = level;
        SessionMessageTitle = title;
        SessionMessage = message;
    }
}
