using Hardcodet.Wpf.TaskbarNotification;
using Musi.Spotify;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace SpotifyMiniPlayer;

public partial class MainWindow : Window
{
    private const double ContainerMargin = 28;

    private string? _accessToken;
    private SpotifyApi? _spotifyApi;

    private readonly System.Windows.Threading.DispatcherTimer _playerTimer;

    private TaskbarIcon? _trayIcon;
    private MenuItem? _trayStartWithWindowsItem;

    private string? _currentTrackUrl;
    private string? _currentCoverUrl;
    private string? _currentTrackName;
    private string? _currentArtistName;

    private bool _vinylIsPlaying;
    private bool _isTrackTransitioning;

    private bool? _lastPlaybackState;

    private bool _spotifyErrorShown;

    public MainWindow()
    {
        InitializeComponent();

        RestoreSettings();

        MouseLeftButtonDown += Window_MouseLeftButtonDown;
        MouseLeftButtonUp += Window_MouseLeftButtonUp;

        _playerTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        _playerTimer.Tick += PlayerTimer_Tick;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        RestoreWindowPosition();

        CreateTrayIcon();
    }

    private void RestoreSettings()
    {
        Topmost = MusiSettingsStore.LoadAlwaysOnTop();

        if (WindowAlwaysOnTopItem != null)
        {
            WindowAlwaysOnTopItem.IsChecked = Topmost;
        }

        bool startWithWindows =
            MusiSettingsStore.LoadStartWithWindows();

        if (WindowStartWithWindowsItem != null)
        {
            WindowStartWithWindowsItem.IsChecked = startWithWindows;
        }
    }

    private void SetStartWithWindows(bool enabled)
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    writable: true);

            if (key == null)
                return;

            const string appName = "Musi";

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;

                if (string.IsNullOrWhiteSpace(exePath))
                    return;

                key.SetValue(
                    appName,
                    $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(
                    appName,
                    throwOnMissingValue: false);
            }

            MusiSettingsStore.SaveStartWithWindows(enabled);

            if (WindowStartWithWindowsItem != null)
            {
                WindowStartWithWindowsItem.IsChecked = enabled;
            }

            if (_trayStartWithWindowsItem != null)
            {
                _trayStartWithWindowsItem.IsChecked = enabled;
            }
        }
        catch
        {
            // Ignore startup setting errors.
        }
    }

    private void SaveSettings()
    {
        MusiSettingsStore.SaveAlwaysOnTop(Topmost);
    }

    private void RestoreWindowPosition()
    {
        (double Left, double Top)? position =
            WindowPositionStore.Load();

        if (position == null)
            return;

        Left =
            position.Value.Left -
            ContainerMargin;

        Top =
            position.Value.Top -
            ContainerMargin;

        // Używamy całego wirtualnego obszaru wszystkich monitorów,
        // a nie tylko głównego monitora. Dzięki temu pozycja może być
        // również ujemna, np. gdy drugi monitor jest po lewej stronie.
        double virtualLeft =
            SystemParameters.VirtualScreenLeft;

        double virtualTop =
            SystemParameters.VirtualScreenTop;

        double virtualRight =
            virtualLeft + SystemParameters.VirtualScreenWidth;

        double virtualBottom =
            virtualTop + SystemParameters.VirtualScreenHeight;

        if (Left < virtualLeft)
            Left = virtualLeft;

        if (Top < virtualTop)
            Top = virtualTop;

        if (Left > virtualRight - Width)
            Left = virtualRight - Width;

        if (Top > virtualBottom - Height)
            Top = virtualBottom - Height;
    }

    private void SetVinylRotation(bool isPlaying)
    {
        if (isPlaying == _vinylIsPlaying)
            return;

        _vinylIsPlaying = isPlaying;

        if (isPlaying)
        {
            if (VinylRotation.HasAnimatedProperties)
                return;

            DoubleAnimation rotation =
                new DoubleAnimation
                {
                    From = VinylRotation.Angle,
                    To = VinylRotation.Angle + 360,
                    Duration =
                        TimeSpan.FromSeconds(12),
                    RepeatBehavior =
                        RepeatBehavior.Forever
                };

            VinylRotation.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                rotation);
        }
        else
        {
            double currentAngle =
                VinylRotation.Angle % 360;

            VinylRotation.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                null);

            VinylRotation.Angle =
                currentAngle;
        }
    }

    private void AnimateCover(BitmapImage image)
    {
        AlbumCoverBrush.ImageSource = image;

        DoubleAnimation fade =
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration =
                    TimeSpan.FromMilliseconds(350),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            };

        AlbumCoverBrush.BeginAnimation(
            ImageBrush.OpacityProperty,
            fade);
    }

    private async Task AnimateTrackTextAsync(
        string title,
        string artist)
    {
        if (_isTrackTransitioning)
            return;

        _isTrackTransitioning = true;

        try
        {
            StopTitleMarquee();

            TranslateTransform titleTransform =
                new TranslateTransform();

            SongTitle.RenderTransform =
                titleTransform;

            TranslateTransform artistTransform =
                new TranslateTransform();

            ArtistName.RenderTransform =
                artistTransform;

            DoubleAnimation oldTitleOpacity =
                new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(160),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        }
                };

            DoubleAnimation oldArtistOpacity =
                new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(160),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        }
                };

            DoubleAnimation oldTitleSlide =
                new DoubleAnimation
                {
                    From = 0,
                    To = -14,
                    Duration =
                        TimeSpan.FromMilliseconds(180),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        }
                };

            DoubleAnimation oldArtistSlide =
                new DoubleAnimation
                {
                    From = 0,
                    To = -14,
                    Duration =
                        TimeSpan.FromMilliseconds(180),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        }
                };

            SongTitle.BeginAnimation(
                OpacityProperty,
                oldTitleOpacity);

            ArtistName.BeginAnimation(
                OpacityProperty,
                oldArtistOpacity);

            titleTransform.BeginAnimation(
                TranslateTransform.XProperty,
                oldTitleSlide);

            artistTransform.BeginAnimation(
                TranslateTransform.XProperty,
                oldArtistSlide);

            await Task.Delay(180);

            SongTitle.Text =
                title;

            ArtistName.Text =
                artist;

            titleTransform.X =
                14;

            artistTransform.X =
                14;

            SongTitle.Opacity =
                0;

            ArtistName.Opacity =
                0;

            DoubleAnimation newTitleOpacity =
                new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration =
                        TimeSpan.FromMilliseconds(280),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        }
                };

            DoubleAnimation newArtistOpacity =
                new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration =
                        TimeSpan.FromMilliseconds(280),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        }
                };

            DoubleAnimation newTitleSlide =
                new DoubleAnimation
                {
                    From = 14,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(320),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        }
                };

            DoubleAnimation newArtistSlide =
                new DoubleAnimation
                {
                    From = 14,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(320),

                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode =
                                EasingMode.EaseOut
                        }
                };

            SongTitle.BeginAnimation(
                OpacityProperty,
                newTitleOpacity);

            ArtistName.BeginAnimation(
                OpacityProperty,
                newArtistOpacity);

            titleTransform.BeginAnimation(
                TranslateTransform.XProperty,
                newTitleSlide);

            artistTransform.BeginAnimation(
                TranslateTransform.XProperty,
                newArtistSlide);

            await Task.Delay(320);

            SongTitle.Opacity =
                1;

            ArtistName.Opacity =
                1;

            titleTransform.X =
                0;

            artistTransform.X =
                0;
        }
        finally
        {
            _isTrackTransitioning =
                false;
        }
    }

    private readonly System.Windows.Threading.DispatcherTimer _titleMarqueeTimer =
        new System.Windows.Threading.DispatcherTimer();

    private double _titleMarqueeDistance;
    private double _titleMarqueePosition;
    private DateTime _titleMarqueeLastTick;
    private DateTime _titleMarqueePhaseStarted;

    private TitleMarqueePhase _titleMarqueePhase =
        TitleMarqueePhase.Stopped;

    private enum TitleMarqueePhase
    {
        Stopped,
        Waiting,
        ScrollingLeft,
        PausedAtEnd,
        ScrollingBack
    }

    private void StopTitleMarquee()
    {
        _titleMarqueeTimer.Stop();

        _titleMarqueePhase =
            TitleMarqueePhase.Stopped;

        _titleMarqueeDistance =
            0;

        _titleMarqueePosition =
            0;

        Canvas.SetLeft(
            SongTitle,
            0);
    }

    private void StartTitleMarquee()
    {
        StopTitleMarquee();

        // Canvas measures its children without constraining their width.
        // Force a layout pass so ActualWidth is the REAL width of the entire title.
        SongTitle.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        SongTitleContainer.UpdateLayout();

        double textWidth =
            SongTitle.ActualWidth;

        if (textWidth <= 0)
        {
            textWidth =
                SongTitle.DesiredSize.Width;
        }

        double viewportWidth =
            SongTitleContainer.ActualWidth;

        if (viewportWidth <= 0)
        {
            viewportWidth =
                177;
        }

        if (textWidth <= viewportWidth + 1)
        {
            Canvas.SetLeft(
                SongTitle,
                0);

            return;
        }

        _titleMarqueeDistance =
            textWidth - viewportWidth;

        _titleMarqueePosition =
            0;

        Canvas.SetLeft(
            SongTitle,
            0);

        _titleMarqueePhase =
            TitleMarqueePhase.Waiting;

        _titleMarqueePhaseStarted =
            DateTime.UtcNow;

        _titleMarqueeLastTick =
            DateTime.UtcNow;

        _titleMarqueeTimer.Interval =
            TimeSpan.FromMilliseconds(16);

        _titleMarqueeTimer.Tick -=
            TitleMarqueeTimer_Tick;

        _titleMarqueeTimer.Tick +=
            TitleMarqueeTimer_Tick;

        _titleMarqueeTimer.Start();
    }

    private void TitleMarqueeTimer_Tick(
        object? sender,
        EventArgs e)
    {
        DateTime now =
            DateTime.UtcNow;

        double deltaTime =
            (now - _titleMarqueeLastTick)
            .TotalSeconds;

        _titleMarqueeLastTick =
            now;

        if (deltaTime <= 0 ||
            deltaTime > 0.1)
        {
            deltaTime =
                0.016;
        }

        const double startDelay =
            1.4;

        const double endPause =
            1.0;

        // Slow, constant movement. No easing = no visual jumping.
        const double scrollSpeed =
            28.0;

        const double returnSpeed =
            42.0;

        switch (_titleMarqueePhase)
        {
            case TitleMarqueePhase.Waiting:

                if ((now -
                     _titleMarqueePhaseStarted)
                    .TotalSeconds >= startDelay)
                {
                    _titleMarqueePhase =
                        TitleMarqueePhase.ScrollingLeft;
                }

                break;

            case TitleMarqueePhase.ScrollingLeft:

                _titleMarqueePosition -=
                    scrollSpeed * deltaTime;

                if (_titleMarqueePosition <=
                    -_titleMarqueeDistance)
                {
                    _titleMarqueePosition =
                        -_titleMarqueeDistance;

                    Canvas.SetLeft(
                        SongTitle,
                        _titleMarqueePosition);

                    _titleMarqueePhase =
                        TitleMarqueePhase.PausedAtEnd;

                    _titleMarqueePhaseStarted =
                        now;

                    break;
                }

                Canvas.SetLeft(
                    SongTitle,
                    _titleMarqueePosition);

                break;

            case TitleMarqueePhase.PausedAtEnd:

                if ((now -
                     _titleMarqueePhaseStarted)
                    .TotalSeconds >= endPause)
                {
                    _titleMarqueePhase =
                        TitleMarqueePhase.ScrollingBack;
                }

                break;

            case TitleMarqueePhase.ScrollingBack:

                _titleMarqueePosition +=
                    returnSpeed * deltaTime;

                if (_titleMarqueePosition >= 0)
                {
                    _titleMarqueePosition =
                        0;

                    Canvas.SetLeft(
                        SongTitle,
                        0);

                    _titleMarqueePhase =
                        TitleMarqueePhase.Waiting;

                    _titleMarqueePhaseStarted =
                        now;

                    break;
                }

                Canvas.SetLeft(
                    SongTitle,
                    _titleMarqueePosition);

                break;
        }
    }

    private async Task AnimatePlayPauseIconAsync(
        bool isPlaying)
    {
        // Pierwsze ustawienie przy uruchomieniu:
        // bez animacji.
        if (_lastPlaybackState == null)
        {
            if (isPlaying)
            {
                PlayIcon.Visibility =
                    Visibility.Collapsed;

                PauseIcon.Visibility =
                    Visibility.Visible;

                PauseIcon.Opacity =
                    1;

                PlayIcon.Opacity =
                    0;
            }
            else
            {
                PauseIcon.Visibility =
                    Visibility.Collapsed;

                PlayIcon.Visibility =
                    Visibility.Visible;

                PlayIcon.Opacity =
                    1;

                PauseIcon.Opacity =
                    0;
            }

            _lastPlaybackState =
                isPlaying;

            return;
        }

        // Nic się nie zmieniło.
        if (_lastPlaybackState == isPlaying)
            return;

        _lastPlaybackState =
            isPlaying;

        UIElement oldIcon;
        UIElement newIcon;

        if (isPlaying)
        {
            oldIcon =
                PlayIcon;

            newIcon =
                PauseIcon;
        }
        else
        {
            oldIcon =
                PauseIcon;

            newIcon =
                PlayIcon;
        }

        // Nowa ikonka pojawia się
        // dokładnie w tym samym miejscu.
        newIcon.Opacity = 0;
        newIcon.Visibility =
            Visibility.Visible;

        DoubleAnimation fadeOut =
            new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration =
                    TimeSpan.FromMilliseconds(140),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseInOut
                    }
            };

        DoubleAnimation fadeIn =
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration =
                    TimeSpan.FromMilliseconds(160),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseInOut
                    }
            };

        oldIcon.BeginAnimation(
            UIElement.OpacityProperty,
            fadeOut);

        newIcon.BeginAnimation(
            UIElement.OpacityProperty,
            fadeIn);

        await Task.Delay(160);

        oldIcon.BeginAnimation(
            UIElement.OpacityProperty,
            null);

        newIcon.BeginAnimation(
            UIElement.OpacityProperty,
            null);

        oldIcon.Opacity =
            0;

        newIcon.Opacity =
            1;

        oldIcon.Visibility =
            Visibility.Collapsed;
    }

    private void OpenCurrentTrackInSpotify()
    {
        if (string.IsNullOrWhiteSpace(_currentTrackUrl))
            return;

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = _currentTrackUrl,
                    UseShellExecute = true
                });
        }
        catch
        {
            // Ignore errors opening Spotify.
        }
    }
    private void ContextMenuShowPlayer_Click(
    object sender,
    RoutedEventArgs e)
    {
        ShowPlayer();
    }

    private void ContextMenuHidePlayer_Click(
        object sender,
        RoutedEventArgs e)
    {
        HidePlayer();
    }

    private void ContextMenuOpenSpotify_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenCurrentTrackInSpotify();
    }

    private void ContextMenuQuit_Click(
        object sender,
        RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
    private void StartWithWindowsMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            SetStartWithWindows(menuItem.IsChecked);
        }
    }

    private void SetSpotifyControlsEnabled(bool enabled)
    {
        PreviousButton.IsEnabled = enabled;
        PlayPauseButton.IsEnabled = enabled;
        NextButton.IsEnabled = enabled;
    }

    private void LogoutSpotify()
    {
        try
        {
            _playerTimer.Stop();

            _spotifyApi = null;
            _accessToken = null;

            string tokenFilePath =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Musi",
                    "spotify_token.dat");

            if (File.Exists(tokenFilePath))
            {
                File.Delete(tokenFilePath);
            }

            StopTitleMarquee();

            SongTitle.Text = "Spotify disconnected";
            ArtistName.Text = "Connect your Spotify account";
            AlbumCoverBrush.ImageSource = null;

            _currentTrackUrl = null;
            _currentCoverUrl = null;
            _currentTrackName = null;
            _currentArtistName = null;

            PlayIcon.Visibility = Visibility.Visible;
            PauseIcon.Visibility = Visibility.Collapsed;

            PlayIcon.Opacity = 1;
            PauseIcon.Opacity = 0;

            _lastPlaybackState = false;

            SetVinylRotation(false);

            ConnectButton.Content = "Connect Spotify";
            ConnectButton.IsEnabled = true;
            ConnectButton.Visibility = Visibility.Visible;

            SetSpotifyControlsEnabled(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Spotify Logout Error");
        }
    }

    private void LogoutSpotifyMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        LogoutSpotify();
    }

    private void AlwaysOnTopMenuItem_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            Topmost =
                menuItem.IsChecked;

            SaveSettings();

            if (WindowAlwaysOnTopItem != null &&
                !ReferenceEquals(
                    sender,
                    WindowAlwaysOnTopItem))
            {
                WindowAlwaysOnTopItem.IsChecked =
                    Topmost;
            }
        }
    }
    private void CreateTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Musi",
            IconSource = new BitmapImage(
    new Uri(
        "pack://application:,,,/Assets/Musi.ico",
        UriKind.Absolute))
        };

        _trayIcon.TrayMouseDoubleClick +=
            TrayIcon_TrayMouseDoubleClick;

        ContextMenu menu =
            new();

        MenuItem showItem =
            new()
            {
                Header = "Show player"
            };

        showItem.Click += (_, _) =>
        {
            ShowPlayer();
        };

        MenuItem hideItem =
            new()
            {
                Header = "Hide player"
            };

        hideItem.Click += (_, _) =>
        {
            HidePlayer();
        };

        MenuItem openSpotifyItem =
            new()
            {
                Header = "Open Spotify"
            };

        openSpotifyItem.Click += (_, _) =>
        {
            OpenCurrentTrackInSpotify();
        };

        MenuItem alwaysOnTopItem =
            new()
            {
                Header = "Always on top",
                IsCheckable = true,
                IsChecked = Topmost
            };

        alwaysOnTopItem.Click +=
            AlwaysOnTopMenuItem_Click;

        MenuItem startWithWindowsItem =
            new()
            {
                Header = "Start with Windows",
                IsCheckable = true,
                IsChecked = MusiSettingsStore.LoadStartWithWindows()
            };

        _trayStartWithWindowsItem = startWithWindowsItem;

        startWithWindowsItem.Click += (_, _) =>
        {
            bool enabled = startWithWindowsItem.IsChecked;
            SetStartWithWindows(enabled);
        };

        MenuItem logoutSpotifyItem =
            new()
            {
                Header = "Log out of Spotify"
            };

        logoutSpotifyItem.Click += (_, _) =>
        {
            LogoutSpotify();
        };

        Separator separator =
            new();

        MenuItem quitItem =
            new()
            {
                Header = "Quit Musi"
            };

        quitItem.Click += (_, _) =>
        {
            Application.Current.Shutdown();
        };

        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(openSpotifyItem);
        menu.Items.Add(alwaysOnTopItem);
        menu.Items.Add(startWithWindowsItem);
        menu.Items.Add(logoutSpotifyItem);
        menu.Items.Add(separator);
        menu.Items.Add(quitItem);

        _trayIcon.ContextMenu =
            menu;
    }

    private void TrayIcon_TrayMouseDoubleClick(
        object sender,
        RoutedEventArgs e)
    {
        ShowPlayer();
    }

    private void ShowPlayer()
    {
        MainContainerTransform.BeginAnimation(
            TranslateTransform.YProperty,
            null);

        MainContainer.BeginAnimation(
            UIElement.OpacityProperty,
            null);

        MainContainerTransform.Y =
            8;

        MainContainer.Opacity =
            0.82;

        Show();

        WindowState =
            WindowState.Normal;

        Activate();

        DoubleAnimation slide =
            new DoubleAnimation
            {
                From = 8,
                To = 0,
                Duration =
                    TimeSpan.FromMilliseconds(200),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            };

        DoubleAnimation fade =
            new DoubleAnimation
            {
                From = 0.82,
                To = 1,
                Duration =
                    TimeSpan.FromMilliseconds(160),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            };

        MainContainerTransform.BeginAnimation(
            TranslateTransform.YProperty,
            slide);

        MainContainer.BeginAnimation(
            UIElement.OpacityProperty,
            fade);
    }

    private void HidePlayer()
    {
        Hide();
    }

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();

        WindowPositionStore.Save(
            Left + ContainerMargin,
            Top + ContainerMargin);

        if (_trayIcon != null)
        {
            _trayIcon.Dispose();

            _trayIcon = null;
        }
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        SpotifyAuth auth =
            new();

        string? accessToken =
            await auth.TryRefreshAccessTokenAsync();

        if (!string.IsNullOrEmpty(accessToken))
        {
            _accessToken =
                accessToken;

            _spotifyApi =
                new SpotifyApi(
                    _accessToken);

            ConnectButton.Visibility =
                Visibility.Collapsed;

            SetSpotifyControlsEnabled(true);

            await UpdatePlayerAsync();

            _playerTimer.Start();
        }
    }

    private void Window_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ButtonState ==
            MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        WindowPositionStore.Save(
            Left + ContainerMargin,
            Top + ContainerMargin);
    }

    private async void ConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ConnectButton.IsEnabled =
                false;

            ConnectButton.Content =
                "Connecting...";

            SpotifyAuth auth =
                new();

            _accessToken =
                await auth.LoginAsync();

            _spotifyApi =
                new SpotifyApi(
                    _accessToken);

            ConnectButton.Visibility =
                Visibility.Collapsed;

            SetSpotifyControlsEnabled(true);

            await UpdatePlayerAsync();

            _playerTimer.Start();
        }
        catch (Exception ex)
        {
            ConnectButton.IsEnabled =
                true;

            ConnectButton.Content =
                "Connect Spotify";

            MessageBox.Show(
                ex.Message,
                "Spotify Error");
        }
    }

    private async Task LoadAlbumCoverAsync(
        string url)
    {
        using HttpClient client =
            new();

        byte[] imageBytes =
            await client.GetByteArrayAsync(
                url);

        using MemoryStream stream =
            new(imageBytes);

        BitmapImage image =
            new();

        image.BeginInit();

        image.StreamSource =
            stream;

        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.EndInit();

        image.Freeze();

        AnimateCover(image);
    }

    private void ShowSpotifyErrorState()
    {
        if (_spotifyErrorShown)
            return;

        _spotifyErrorShown = true;

        StopTitleMarquee();

        SongTitle.Text = "Spotify unavailable";
        ArtistName.Text = "Trying to reconnect...";
        AlbumCoverBrush.ImageSource = null;

        _currentTrackUrl = null;
        _currentCoverUrl = null;
        _currentTrackName = null;
        _currentArtistName = null;

        PlayIcon.Visibility = Visibility.Visible;
        PauseIcon.Visibility = Visibility.Collapsed;
        PlayIcon.Opacity = 1;
        PauseIcon.Opacity = 0;

        SetVinylRotation(false);
        SetSpotifyControlsEnabled(false);
    }

    private void RestoreSpotifyErrorState()
    {
        if (!_spotifyErrorShown)
            return;

        _spotifyErrorShown = false;
        SetSpotifyControlsEnabled(true);
    }

    private void HandleSpotifyError()
    {
        ShowSpotifyErrorState();
    }

    private async Task UpdatePlayerAsync()
    {
        if (_spotifyApi == null)
            return;

        try
        {
            SpotifyTrack? track =
                await _spotifyApi
                    .GetCurrentlyPlayingAsync();

            RestoreSpotifyErrorState();

            if (track == null)
            {
                StopTitleMarquee();

                SongTitle.Text =
                    "Nothing playing";

                ArtistName.Text =
                    "";

                AlbumCoverBrush.ImageSource =
                    null;

                _currentTrackUrl =
                    null;

                _currentCoverUrl =
                    null;

                _currentTrackName =
                    null;

                _currentArtistName =
                    null;

                PlayIcon.Visibility =
                    Visibility.Visible;

                PauseIcon.Visibility =
                    Visibility.Collapsed;

                PlayIcon.Opacity =
                    1;

                PauseIcon.Opacity =
                    0;

                _lastPlaybackState =
                    false;

                SetVinylRotation(
                    false);

                return;
            }

            bool trackChanged =
                _currentTrackName !=
                    track.Name ||
                _currentArtistName !=
                    track.Artist;

            if (trackChanged &&
                _currentTrackName != null)
            {
                await AnimateTrackTextAsync(
                    track.Name,
                    track.Artist);

                StartTitleMarquee();
            }
            else
            {
                SongTitle.Text =
                    track.Name;

                ArtistName.Text =
                    track.Artist;

                if (_currentTrackName == null)
                {
                    StartTitleMarquee();
                }
            }

            _currentTrackName =
                track.Name;

            _currentArtistName =
                track.Artist;

            _currentTrackUrl =
                track.TrackUrl;

            if (!string.IsNullOrEmpty(
                    track.CoverUrl) &&
                track.CoverUrl !=
                    _currentCoverUrl)
            {
                _currentCoverUrl =
                    track.CoverUrl;

                await LoadAlbumCoverAsync(
                    track.CoverUrl);
            }

            await AnimatePlayPauseIconAsync(
                track.IsPlaying);

            SetVinylRotation(
                track.IsPlaying);
        }
        catch (HttpRequestException)
        {
            HandleSpotifyError();
        }
        catch (TaskCanceledException)
        {
            HandleSpotifyError();
        }
        catch
        {
            HandleSpotifyError();
        }
    }

    private void Vinyl_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled =
            true;
    }

    private void Vinyl_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled =
            true;

        if (string.IsNullOrEmpty(
                _currentTrackUrl))
            return;

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        _currentTrackUrl,

                    UseShellExecute =
                        true
                });
        }
        catch
        {
            // Ignore errors opening Spotify.
        }
    }

    private async void PlayerTimer_Tick(
        object? sender,
        EventArgs e)
    {
        await UpdatePlayerAsync();
    }

    private async void PreviousButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_spotifyApi == null)
            return;

        try
        {
            await _spotifyApi
                .PreviousAsync();

            await Task.Delay(500);

            await UpdatePlayerAsync();
        }
        catch (HttpRequestException)
        {
            HandleSpotifyError();
        }
        catch (TaskCanceledException)
        {
            HandleSpotifyError();
        }
        catch
        {
            HandleSpotifyError();
        }
    }

    private async void PlayPauseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_spotifyApi == null)
            return;

        try
        {
            SpotifyTrack? track =
                await _spotifyApi
                    .GetCurrentlyPlayingAsync();

            if (track == null)
                return;

            if (track.IsPlaying)
            {
                await _spotifyApi
                    .PauseAsync();
            }
            else
            {
                await _spotifyApi
                    .PlayAsync();
            }

            await Task.Delay(300);

            await UpdatePlayerAsync();
        }
        catch (HttpRequestException)
        {
            HandleSpotifyError();
        }
        catch (TaskCanceledException)
        {
            HandleSpotifyError();
        }
        catch
        {
            HandleSpotifyError();
        }
    }

    private async void NextButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_spotifyApi == null)
            return;

        try
        {
            await _spotifyApi
                .NextAsync();

            await Task.Delay(500);

            await UpdatePlayerAsync();
        }
        catch (HttpRequestException)
        {
            HandleSpotifyError();
        }
        catch (TaskCanceledException)
        {
            HandleSpotifyError();
        }
        catch
        {
            HandleSpotifyError();
        }
    }
}