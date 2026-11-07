using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    private string _dropLabelText = "Перетащите иконку игры, чтобы указать путь к папке";
    private bool _isGameFound = false;
    private string _spaceRequiredText = "Требуется места: 900 Мбайт";
    private string _spaceAvailableText = "Доступно места: -- Мбайт";
    private bool _isAdminWarningVisible = false;

    // Installation
    private int _progressPercentage = 0;
    private int _targetProgress = 0;
    private string _statusText = "Подготовка к установке...";
    private string _detailedLogs = string.Empty;
    private bool _isDetailsVisible = false;
    private bool _isInstallFinished = false;
    private Bitmap? _statusMascot;
    private readonly DispatcherTimer _smoothTimer;
    private readonly DispatcherTimer _mascotTimer;
    private List<(Bitmap frame, int delayMs)> _mascotFrames = new();
    private int _currentMascotFrameIndex = 0;

    // Interactive Warning
    private bool _isWarningModalVisible = false;
    private string _warningTitle = string.Empty;
    private string _warningSubtitle = string.Empty;
    private TaskCompletionSource<bool>? _warningTcs;

    // End Success / Fail
    private bool _startGameChecked = true;
    private string _errorCodeText = string.Empty;
    private string _errorDetailsText = string.Empty;

    // Suite config
    private PatchSuiteConfig _suiteConfig = new();
    private CancellationTokenSource? _cts;

    public MainWindowViewModel()
    {
        // 1. Load theme assets
        IntroBanner = ThemeManager.GetBitmap("preview.png");
        HeaderIcon = ThemeManager.GetBitmap("icon.png");
        DefaultDropIcon = ThemeManager.GetBitmap("drop_icon.png");
        GameFoundDropIcon = ThemeManager.GetBitmap("drop_icon_game.png");
        ErrorDropIcon = ThemeManager.GetBitmap("drop_icon_err.png");
        CurrentDropIcon = DefaultDropIcon;

        SuccessImage = ThemeManager.GetBitmap("queen_sprite.png");
        FailImage = ThemeManager.GetBitmap("queen_sprite_error.png");
        WarningImage = ThemeManager.GetBitmap("mnogo_voprosoff_warning.png");

        // 2. Load animated mascot frames
        _mascotFrames = ThemeManager.GetGifFrames("lytaya_animka_ralsei2009.gif");
        if (_mascotFrames.Count > 0)
        {
            StatusMascot = _mascotFrames[0].frame;
        }
        else
        {
            StatusMascot = ThemeManager.GetBitmap("icon.png");
        }

        // 3. Setup mascot animation timer
        _mascotTimer = new DispatcherTimer();
        _mascotTimer.Tick += OnMascotTimerTick;
        if (_mascotFrames.Count > 1)
        {
            _mascotTimer.Interval = TimeSpan.FromMilliseconds(_mascotFrames[0].delayMs);
            _mascotTimer.Start();
        }

        // 4. Setup smooth progress timer
        _smoothTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        _smoothTimer.Tick += OnSmoothTimerTick;

        // 5. Check administrator privilege
        IsAdminWarningVisible = AdminPrivilegeHelper.IsAdministrator;

        // 6. Hook log events to detailed logs
        Out.OnLog += OnOutLogReceived;

        // 7. Try load patch_suite.json or auto-discover
        LoadDefaultSuite();

        // 8. Auto-search Steam installation
        SearchInitialGameFolder();
    }

    #region Properties

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
            }
        }
    }

    public bool IsIntro => CurrentStep == WizardStep.Intro;
    public bool IsDropLink => CurrentStep == WizardStep.DropLink;
    public bool IsSelectPath => CurrentStep == WizardStep.SelectPath;
    public bool IsInstallation => CurrentStep == WizardStep.Installation;
    public bool IsEndSuccess => CurrentStep == WizardStep.EndSuccess;
    public bool IsEndFail => CurrentStep == WizardStep.EndFail;

    public string WindowTitle => ThemeManager.Config.Texts.WindowTitle;
    public string IntroTitle => ThemeManager.Config.Texts.IntroTitle;
    public string IntroSubtitle => ThemeManager.Config.Texts.IntroSubtitle;
    public string Version => ThemeManager.Config.Texts.Version;
    public string IssuesUrl => ThemeManager.Config.Links.Issues;
    public string CommunityUrl => ThemeManager.Config.Links.Community;
    public string CommunityText => ThemeManager.Config.Links.CommunityText;
    public string SuccessTitle => ThemeManager.Config.Texts.SuccessTitle;
    public string SuccessSubtitle => ThemeManager.Config.Texts.SuccessSubtitle;
    public string StartGameCheckbox => ThemeManager.Config.Texts.StartGameCheckbox;

    public Bitmap? IntroBanner { get; }
    public Bitmap? HeaderIcon { get; }
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
        set => SetField(ref _isDetailsVisible, value);
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

    public void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
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
        catch { }
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
            DropLabelText = "Перетащите иконку игры, чтобы указать путь к папке";
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

            long freeSpace = SafeFileOperations.GetFreeSpaceMB(normalized);
            SpaceAvailableText = freeSpace >= 0 ? $"Доступно места: {freeSpace} Мбайт" : "Доступно места: -- Мбайт";
            SpaceRequiredText = $"Требуется места: {_suiteConfig.RequiredSpaceMB} Мбайт";
        }
        else
        {
            SelectedGameFolder = normalized;
            IsGameFound = false;
            CurrentDropIcon = ErrorDropIcon;
            DropLabelText = validation.ErrorMessage ?? "Не удалось найти игру, попробуйте ещё раз";
            SpaceAvailableText = "Доступно места: -- Мбайт";
        }
    }

    private void SearchInitialGameFolder()
    {
        string? steamFound = SteamLibraryDetector.FindGameDirectory("DELTARUNE", "1672970");
        if (!string.IsNullOrEmpty(steamFound))
        {
            EvaluateGameFolder(steamFound);
        }
    }

    private void LoadDefaultSuite()
    {
        string baseDir = AppContext.BaseDirectory;
        string suitePath = Path.Combine(baseDir, "patch_suite.json");

        if (File.Exists(suitePath))
        {
            var loaded = PatchSuiteConfig.LoadFromFile(suitePath);
            if (loaded != null)
                _suiteConfig = loaded;
        }
        else
        {
            // Default built-in DELTARUNE fallback suite
            _suiteConfig = new PatchSuiteConfig
            {
                Name = "DELTARUNE: Ручной перевод",
                GameId = "deltarune",
                RequiredSpaceMB = 850,
                Targets = new List<PatchTargetConfig>
                {
                    new()
                    {
                        Id = "selector",
                        Title = "Выбор главы",
                        RelativePath = "data.win",
                        MacRelativePath = "game.ios",
                        PatchConfig = "patches/ch_sel/data.win.json",
                        BackupFile = "data.orig.win",
                        MacBackupFile = "game.orig.ios",
                        Required = true
                    },
                    new()
                    {
                        Id = "ch1",
                        Title = "Глава 1",
                        RelativePath = "chapter1_windows/data.win",
                        MacRelativePath = "chapter1_mac/game.ios",
                        PatchConfig = "patches/ch1/data.win.json",
                        BackupFile = "chapter1_windows/data.orig.win",
                        MacBackupFile = "chapter1_mac/game.orig.ios",
                        Required = true
                    },
                    new()
                    {
                        Id = "ch2",
                        Title = "Глава 2",
                        RelativePath = "chapter2_windows/data.win",
                        MacRelativePath = "chapter2_mac/game.ios",
                        PatchConfig = "patches/ch2/data.win.json",
                        BackupFile = "chapter2_windows/data.orig.win",
                        MacBackupFile = "chapter2_mac/game.orig.ios",
                        Required = false
                    }
                }
            };
        }
    }

    #endregion

    #region Installation Execution

    private void StartInstallation()
    {
        ProgressPercentage = 0;
        _targetProgress = 0;
        StatusText = "Запуск процесса установки...";
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

                int result = await MultiPatchRunner.ApplySuiteAsync(
                    SelectedGameFolder,
                    _suiteConfig,
                    progressReporter,
                    OnWarningConfirmationRequested,
                    _cts.Token
                );

                Dispatcher.UIThread.Post(() =>
                {
                    if (result == 0)
                    {
                        _targetProgress = 100;
                        ProgressPercentage = 100;
                        StatusText = "Патчинг завершён!";
                        IsInstallFinished = true;
                        CurrentStep = WizardStep.EndSuccess;
                    }
                    else
                    {
                        OnInstallationFailed(result, $"Ошибка установки (код {result})");
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
                204 => "Обнаружена модификация!",
                205 => "Версия игры и перевода не совпадает!",
                206 => "Версия игры устарела!",
                _ => "Внимание!"
            };

            WarningSubtitle = message;
            _warningTcs = new TaskCompletionSource<bool>();
            IsWarningModalVisible = true;

            return await _warningTcs.Task;
        });
    }

    private void OnInstallationFailed(int errorCode, string message)
    {
        StatusMascot = ThemeManager.GetBitmap("ralsei_down.png");
        _mascotTimer.Stop();

        ErrorCodeText = $"Код ошибки: {errorCode}";
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
                string exeName = ThemeManager.Config.Texts.GameExecutable;
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

