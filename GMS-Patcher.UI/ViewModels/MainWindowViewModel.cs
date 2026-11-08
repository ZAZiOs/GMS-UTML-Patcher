using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GMS_Patcher.UI.Services;
using GMS_UTML_Patcher.Engine;
using GMS_UTML_Patcher.Services;

namespace GMS_Patcher.UI.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private WizardStep _currentStep = WizardStep.Intro;
    private string _selectedGameFolder = string.Empty;
    private string _pathFieldText = string.Empty;
    private string _dropLabelText = string.Empty;
    private string _dropClassicLinkText = string.Empty;
    private bool _isGameFound = false;
    private string _spaceRequiredText = string.Empty;
    private string _spaceAvailableText = string.Empty;
    private bool _isAdminWarningVisible = false;

    private int _progressPercentage = 0;
    private int _targetProgress = 0;
    private string _statusText = string.Empty;
    private string _detailedLogs = string.Empty;
    private bool _isDetailsVisible = false;
    private bool _isInstallFinished = false;
    private Bitmap? _statusMascot;
    private readonly DispatcherTimer _smoothTimer;
    private readonly DispatcherTimer _mascotTimer;
    private List<(Bitmap frame, int delayMs)> _mascotFrames = new();
    private int _currentMascotFrameIndex = 0;

    private bool _isWarningModalVisible = false;
    private string _warningTitle = string.Empty;
    private string _warningSubtitle = string.Empty;
    private TaskCompletionSource<bool>? _warningTcs;

    private bool _startGameChecked = true;
    private string _errorCodeText = string.Empty;
    private string _errorDetailsText = string.Empty;

    private PatchSuiteConfig _suiteConfig = new();
    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {

        _dropLabelText = ThemeManager.DropDefaultLabel;
        _dropClassicLinkText = ThemeManager.DropClassicLink;
        _statusText = ThemeManager.InstallInitialStatus;
        string reqFormatted = _suiteConfig.RequiredSpaceMB >= 1024
    ? string.Create(CultureInfo.InvariantCulture, $"{_suiteConfig.RequiredSpaceMB / 1024.0:0.0} GB")
    : string.Create(CultureInfo.InvariantCulture, $"{_suiteConfig.RequiredSpaceMB:0.0} MB");
        _spaceRequiredText = ThemeManager.SpaceRequired(reqFormatted);
        _spaceAvailableText = ThemeManager.SpaceNoDisk;

        IntroBanner = ThemeManager.GetImageBitmap("banner", "image1.png", "preview.png", "banner.png", "image_intro.png");
        HeaderBanner = ThemeManager.GetImageBitmap("headerBanner", "headerIcon", "icon.png", "header.png", "header_banner.png");
        WindowIcon = ThemeManager.GetWindowIcon("exe-icon", "main.ico", "cozyinn.ico", "app.ico");
        DefaultDropIcon = ThemeManager.GetImageBitmap("dropDefault", "drop_icon.png", "drop.png");
        GameFoundDropIcon = ThemeManager.GetImageBitmap("dropSuccess", "drop_icon_game.png", "drop_game.png");
        ErrorDropIcon = ThemeManager.GetImageBitmap("dropError", "drop_icon_err.png", "drop_error.png");
        CurrentDropIcon = DefaultDropIcon;

        SuccessImage = ThemeManager.GetImageBitmap("bannerSuccess", "image1.png", "preview.png", "banner.png", "queen_sprite.png");
        FailImage = ThemeManager.GetImageBitmap("bannerFail", "image2.png", "preview2.png", "banner_error.png", "queen_sprite_error.png");
        WarningImage = ThemeManager.GetImageBitmap("warning", "mnogo_voprosoff_warning.png", "warning.png");

        _mascotFrames = ThemeManager.GetGifAnimation("mascot", "lytaya_animka_ralsei2009.gif", "mascot.gif", "animation.gif");
        if (_mascotFrames.Count > 0)
        {
            StatusMascot = _mascotFrames[0].frame;
        }
        else
        {
            StatusMascot = ThemeManager.GetBitmap("icon.png");
        }

        _mascotTimer = new DispatcherTimer();
        _mascotTimer.Tick += OnMascotTimerTick;
        if (_mascotFrames.Count > 1)
        {
            _mascotTimer.Interval = TimeSpan.FromMilliseconds(_mascotFrames[0].delayMs);
            _mascotTimer.Start();
        }

        _smoothTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        _smoothTimer.Tick += OnSmoothTimerTick;

        IsAdminWarningVisible = AdminPrivilegeHelper.IsAdministrator;

        Out.OnLog += OnOutLogReceived;

        LoadDefaultSuite();
        UpdateSpaceIndicators(null);

        SearchInitialGameFolder();
    }

    #region Properties & Localized Strings

    public WizardStep CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetField(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsIntro));
                OnPropertyChanged(nameof(IsDropLink));
                OnPropertyChanged(nameof(IsSelectPath));
                OnPropertyChanged(nameof(IsInstallation));
                OnPropertyChanged(nameof(IsEndSuccess));
                OnPropertyChanged(nameof(IsEndFail));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(NextButtonText));
                OnPropertyChanged(nameof(ShowLeftCancelButton));
                OnPropertyChanged(nameof(ShowBackButton));
                OnPropertyChanged(nameof(ShowNextButton));
            }
        }
    }

    public bool IsIntro => CurrentStep == WizardStep.Intro;
    public bool IsDropLink => CurrentStep == WizardStep.DropLink;
    public bool IsSelectPath => CurrentStep == WizardStep.SelectPath;
    public bool IsInstallation => CurrentStep == WizardStep.Installation;
    public bool IsEndSuccess => CurrentStep == WizardStep.EndSuccess;
    public bool IsEndFail => CurrentStep == WizardStep.EndFail;

    public bool ShowLeftCancelButton => !IsEndSuccess && !IsEndFail;
    public bool ShowBackButton => !IsIntro && !IsEndSuccess && !IsEndFail;
    public bool ShowNextButton => !IsEndSuccess && !IsEndFail;

    public string WindowTitle => ThemeManager.WindowTitle;
    public string IntroTitle => ThemeManager.IntroTitle;
    public string IntroSubtitle => ThemeManager.IntroSubtitle;
    public string IntroIssuesLink => ThemeManager.IntroIssuesLink;
    public string IntroVersionLabel => ThemeManager.IntroVersionLabel;
    public string IntroNextPrompt => ThemeManager.IntroNextPrompt;
    public string Version => ThemeManager.Version;

    public string DropHeaderTitle => ThemeManager.DropHeaderTitle;
    public string DropHeaderSubtitle => ThemeManager.DropHeaderSubtitle;
    public string DropGroupBoxTitle => ThemeManager.DropGroupBoxTitle;
    public string CopyrightText => ThemeManager.Copyright;
    public string DropClassicLinkText
    {
        get => _dropClassicLinkText;
        set => SetField(ref _dropClassicLinkText, value);
    }

    public string PathHeaderTitle => ThemeManager.PathHeaderTitle;
    public string PathHeaderSubtitle => ThemeManager.PathHeaderSubtitle;
    public string PathLabel => ThemeManager.PathLabel;
    public string PathBrowseBtn => ThemeManager.PathBrowseBtn;
    public string PathBackToDropLink => ThemeManager.PathBackToDropLink;

    public string InstallHeaderTitle => ThemeManager.InstallHeaderTitle;
    public string InstallHeaderSubtitle => ThemeManager.InstallHeaderSubtitle;

    public string SuccessTitle => ThemeManager.SuccessTitle;
    public string SuccessSubtitle => ThemeManager.SuccessSubtitle;
    public string CommunityLink => ThemeManager.CommunityLink;
    public string CommunityText => ThemeManager.CommunityLink;
    public string SuccessFinishPrompt => ThemeManager.SuccessFinishPrompt;
    public string StartGameCheckbox => ThemeManager.StartGameCheckbox;

    public string FailTitle => ThemeManager.FailTitle;
    public string FailIssuesLink => ThemeManager.FailIssuesLink;

    public string BtnNext => ThemeManager.BtnNext;
    public string BtnBack => ThemeManager.BtnBack;
    public string BtnInstall => ThemeManager.BtnInstall;
    public string BtnExit => ThemeManager.BtnExit;
    public string BtnCancel => ThemeManager.BtnCancel;
    public string BtnFinish => ThemeManager.BtnFinish;

    public string WarningContinue => ThemeManager.WarningContinue;
    public string WarningCancel => ThemeManager.WarningCancel;

    public string AdminWarningText => ThemeManager.AdminWarning;
    public string IssuesUrl => ThemeManager.IssuesUrl;
    public string CommunityUrl => ThemeManager.CommunityUrl;

    public Bitmap? IntroBanner { get; }
    public Bitmap? HeaderBanner { get; }
    public Bitmap? HeaderIcon => HeaderBanner;
    public Bitmap? ExeIcon => HeaderBanner;
    public WindowIcon? WindowIcon { get; }
    public Bitmap? DefaultDropIcon { get; }
    public Bitmap? GameFoundDropIcon { get; }
    public Bitmap? ErrorDropIcon { get; }
    public Bitmap? SuccessImage { get; }
    public Bitmap? FailImage { get; }
    public Bitmap? WarningImage { get; }

    private Bitmap? _currentDropIcon;
    public Bitmap? CurrentDropIcon
    {
        get => _currentDropIcon;
        set => SetField(ref _currentDropIcon, value);
    }

    public Bitmap? StatusMascot
    {
        get => _statusMascot;
        set => SetField(ref _statusMascot, value);
    }

    public string SelectedGameFolder
    {
        get => _selectedGameFolder;
        set => SetField(ref _selectedGameFolder, value);
    }

    public string PathFieldText
    {
        get => _pathFieldText;
        set
        {
            if (SetField(ref _pathFieldText, value))
            {
                EvaluateGameFolder(value);
            }
        }
    }

    public string DropLabelText
    {
        get => _dropLabelText;
        set => SetField(ref _dropLabelText, value);
    }

    public bool IsGameFound
    {
        get => _isGameFound;
        set
        {
            if (SetField(ref _isGameFound, value))
            {
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public bool CanGoNext => CurrentStep switch
    {
        WizardStep.Intro => true,
        WizardStep.DropLink => IsGameFound,
        WizardStep.SelectPath => IsGameFound,
        WizardStep.Installation => IsInstallFinished,
        _ => true
    };

    public string NextButtonText => CurrentStep switch
    {
        WizardStep.DropLink => ThemeManager.BtnInstall,
        WizardStep.SelectPath => ThemeManager.BtnInstall,
        _ => ThemeManager.BtnNext
    };

    public string DetailsButtonText => IsDetailsVisible ? ThemeManager.DetailsBtnHide : ThemeManager.DetailsBtnShow;

    public bool IsAdminWarningVisible
    {
        get => _isAdminWarningVisible;
        set => SetField(ref _isAdminWarningVisible, value);
    }

    public string SpaceRequiredText
    {
        get => _spaceRequiredText;
        set => SetField(ref _spaceRequiredText, value);
    }

    public string SpaceAvailableText
    {
        get => _spaceAvailableText;
        set => SetField(ref _spaceAvailableText, value);
    }

    public int ProgressPercentage
    {
        get => _progressPercentage;
        set
        {
            if (SetField(ref _progressPercentage, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string ProgressText => $"{ProgressPercentage}%";

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string DetailedLogs
    {
        get => _detailedLogs;
        set => SetField(ref _detailedLogs, value);
    }

    public bool IsDetailsVisible
    {
        get => _isDetailsVisible;
        set
        {
            if (SetField(ref _isDetailsVisible, value))
            {
                OnPropertyChanged(nameof(DetailsButtonText));
            }
        }
    }

    public bool IsInstallFinished
    {
        get => _isInstallFinished;
        set
        {
            if (SetField(ref _isInstallFinished, value))
            {
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public bool IsWarningModalVisible
    {
        get => _isWarningModalVisible;
        set => SetField(ref _isWarningModalVisible, value);
    }

    public string WarningTitle
    {
        get => _warningTitle;
        set => SetField(ref _warningTitle, value);
    }

    public string WarningSubtitle
    {
        get => _warningSubtitle;
        set => SetField(ref _warningSubtitle, value);
    }

    public bool StartGameChecked
    {
        get => _startGameChecked;
        set => SetField(ref _startGameChecked, value);
    }

    public string ErrorCodeText
    {
        get => _errorCodeText;
        set => SetField(ref _errorCodeText, value);
    }

    public string ErrorDetailsText
    {
        get => _errorDetailsText;
        set => SetField(ref _errorDetailsText, value);
    }

    #endregion

    #region Navigation & Actions

    public void GoNext()
    {
        switch (CurrentStep)
        {
            case WizardStep.Intro:
                CurrentStep = WizardStep.DropLink;
                break;
            case WizardStep.DropLink:
            case WizardStep.SelectPath:
                CurrentStep = WizardStep.Installation;
                StartInstallation();
                break;
            case WizardStep.Installation:
                CurrentStep = WizardStep.EndSuccess;
                break;
        }
    }

    public void GoBack()
    {
        switch (CurrentStep)
        {
            case WizardStep.DropLink:
            case WizardStep.SelectPath:
                CurrentStep = WizardStep.Intro;
                break;
            case WizardStep.Installation:
                break;
        }
    }

    public void SwitchToClassicMode()
    {
        CurrentStep = WizardStep.SelectPath;
    }

    public void SwitchToDropMode()
    {
        CurrentStep = WizardStep.DropLink;
    }

    public void ToggleDetails()
    {
        IsDetailsVisible = !IsDetailsVisible;
    }

    public void OnFinishClicked(Action closeWindow)
    {
        if (StartGameChecked)
        {
            LaunchGame();
        }
        closeWindow();
    }

    public void OpenIssuesUrl() => OpenUrl(IssuesUrl);

    public void OpenCommunityUrl() => OpenUrl(CommunityUrl);

    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = IssuesUrl;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return;
                }
                catch
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start \"\" \"{url.Replace("&", "^&")}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open URL '{url}': {ex.Message}");
        }
    }

    public void ConfirmWarning()
    {
        WarningConfirm(true);
    }

    public void CancelWarning()
    {
        WarningConfirm(false);
    }

    public void WarningConfirm(bool continuePatching)
    {
        IsWarningModalVisible = false;
        _warningTcs?.TrySetResult(continuePatching);
    }

    #endregion

    #region Game Detection & Path Handling

    public void HandleDroppedFile(string rawPath)
    {
        EvaluateGameFolder(rawPath);
    }

    public void EvaluateGameFolder(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            IsGameFound = false;
            CurrentDropIcon = DefaultDropIcon;
            DropLabelText = ThemeManager.DropDefaultLabel;
            DropClassicLinkText = ThemeManager.DropClassicLink;
            UpdateSpaceIndicators(null);
            return;
        }

        string normalized = MultiPatchRunner.NormalizeGamePath(rawPath);
        var validation = MultiPatchRunner.ValidateGameFolder(normalized, _suiteConfig);

        if (validation.IsValid)
        {
            SelectedGameFolder = normalized;
            if (_pathFieldText != normalized)
            {
                _pathFieldText = normalized;
                OnPropertyChanged(nameof(PathFieldText));
            }

            IsGameFound = true;
            CurrentDropIcon = GameFoundDropIcon;
            DropLabelText = normalized;
            DropClassicLinkText = ThemeManager.DropFoundClassicLink;
            UpdateSpaceIndicators(normalized);
        }
        else
        {
            SelectedGameFolder = normalized;
            IsGameFound = false;
            CurrentDropIcon = ErrorDropIcon;
            DropLabelText = validation.ErrorMessage ?? ThemeManager.DropErrorLabel;
            DropClassicLinkText = ThemeManager.DropClassicLink;
            UpdateSpaceIndicators(normalized);
        }
    }

    private void UpdateSpaceIndicators(string? folderPath)
    {
        string reqFormatted = _suiteConfig.RequiredSpaceMB >= 1024
            ? $"{(_suiteConfig.RequiredSpaceMB / 1024.0):0.0} GB"
            : $"{_suiteConfig.RequiredSpaceMB:0.0} MB";
        SpaceRequiredText = ThemeManager.SpaceRequired(reqFormatted);

        string probePath = !string.IsNullOrWhiteSpace(folderPath)
            ? folderPath
            : AppDomain.CurrentDomain.BaseDirectory;

        long freeSpace = SafeFileOperations.GetFreeSpaceMB(probePath);
        if (freeSpace >= 0)
        {
            string freeFormatted = freeSpace >= 1024
                ? $"{(freeSpace / 1024.0):0.0} GB"
                : $"{freeSpace:0.0} MB";
            SpaceAvailableText = ThemeManager.SpaceAvailable(freeFormatted);
        }
        else
        {
            SpaceAvailableText = ThemeManager.SpaceNoDisk;
        }
    }

    private void SearchInitialGameFolder()
    {
        string? steamFound = SteamLibraryDetector.FindGameDirectory(ThemeManager.GameName);
        if (!string.IsNullOrEmpty(steamFound))
        {
            EvaluateGameFolder(steamFound);
        }
    }

    private void LoadDefaultSuite()
    {
        string? suitePath = PatchSuiteConfig.FindSuiteConfigFile();
        if (!string.IsNullOrEmpty(suitePath))
        {
            var loaded = PatchSuiteConfig.LoadFromFile(suitePath);
            if (loaded != null)
            {
                _suiteConfig = loaded;
                return;
            }
        }

        _suiteConfig = new PatchSuiteConfig
        {
            Name = $"{ThemeManager.GameName} Patch",
            GameId = "game",
            RequiredSpaceMB = 500,
            Targets = new List<PatchTargetConfig>
            {
                new()
                {
                    Id = "main",
                    Title = "Main Game",
                    RelativePath = "data.win",
                    MacRelativePath = "game.ios",
                    PatchConfig = "patches/data.win.json",
                    MacPatchConfig = "patches/game.ios.json",
                    BackupFile = "data.orig.win",
                    MacBackupFile = "game.orig.ios",
                    Required = true
                }
            }
        };
    }

    #endregion

    #region Installation Execution

    private void StartInstallation()
    {
        ProgressPercentage = 0;
        _targetProgress = 0;
        StatusText = ThemeManager.InstallInitialStatus;
        IsInstallFinished = false;
        _smoothTimer.Start();

        _cts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                var progressReporter = new Progress<PatchProgress>(p =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _targetProgress = Math.Clamp(p.Percentage, 0, 100);
                        StatusText = p.Status;
                    });
                });

                var patchResult = await MultiPatchRunner.ApplySuiteAsync(
                    SelectedGameFolder,
                    _suiteConfig,
                    progressReporter,
                    OnWarningConfirmationRequested,
                    _cts.Token
                );

                Dispatcher.UIThread.Post(() =>
                {
                    if (patchResult.ExitCode == 0)
                    {
                        _targetProgress = 100;
                        ProgressPercentage = 100;
                        StatusText = ThemeManager.InstallFinishedStatus;
                        IsInstallFinished = true;
                        CurrentStep = WizardStep.EndSuccess;
                    }
                    else
                    {
                        OnInstallationFailed(patchResult.ExitCode, patchResult.ErrorMessage ?? $"Installation failed (code {patchResult.ExitCode})");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OnInstallationFailed(500, ex.Message);
                });
            }
        });
    }

    private async Task<bool> OnWarningConfirmationRequested(int errorCode, string message)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            WarningTitle = errorCode switch
            {
                204 => ThemeManager.WarningTitleMod,
                205 or 206 => ThemeManager.WarningTitleVersion,
                _ => "Внимание!"
            };

            WarningSubtitle = errorCode switch
            {
                204 => ThemeManager.WarningMessageMod,
                205 => ThemeManager.WarningMessageNewerVersion,
                206 => ThemeManager.WarningMessageVersionMismatch,
                _ => string.IsNullOrWhiteSpace(message) ? ThemeManager.WarningMessageGeneric : message
            };

            _warningTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsWarningModalVisible = true;

            return await _warningTcs.Task;
        });
    }

    private void OnInstallationFailed(int errorCode, string message)
    {
        _smoothTimer.Stop();
        _mascotTimer.Stop();
        StatusMascot = ThemeManager.GetImageBitmap("mascotFail", "ralsei_down.png", "mascot_fail.png", "fail.png");

        ErrorCodeText = ThemeManager.Get("errorCodeFormat", ("{CODE}", errorCode.ToString()));
        ErrorDetailsText = message;
        IsInstallFinished = true;
        IsDetailsVisible = true;
        CurrentStep = WizardStep.EndFail;
    }

    private void LaunchGame()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string exeName = ThemeManager.GameExe;
                string exePath = Path.Combine(SelectedGameFolder, exeName);
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath)
                    {
                        WorkingDirectory = SelectedGameFolder,
                        UseShellExecute = true
                    });
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", SelectedGameFolder);
            }
        }
        catch { }
    }

    #endregion

    #region Timers & Logging

    private void OnSmoothTimerTick(object? sender, EventArgs e)
    {
        if (ProgressPercentage < _targetProgress)
        {
            ProgressPercentage++;
        }
        else if (ProgressPercentage == 100 && IsInstallFinished)
        {
            _smoothTimer.Stop();
        }
    }

    private void OnMascotTimerTick(object? sender, EventArgs e)
    {
        if (_mascotFrames.Count <= 1) return;

        _currentMascotFrameIndex = (_currentMascotFrameIndex + 1) % _mascotFrames.Count;
        var frame = _mascotFrames[_currentMascotFrameIndex];
        StatusMascot = frame.frame;
        _mascotTimer.Interval = TimeSpan.FromMilliseconds(frame.delayMs);
    }

    private void OnOutLogReceived(LogMessageEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DetailedLogs += e.FullMessage + Environment.NewLine;
        });
    }

    #endregion
}
