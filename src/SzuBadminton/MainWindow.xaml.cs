using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using SzuBadminton.Models;
using SzuBadminton.Services;

namespace SzuBadminton;

public partial class MainWindow : Window
{
    private const string VenueUrl = "https://ehall.szu.edu.cn/qljfwapp/sys/lwSzuCgyy/index.do#/sportVenue";
    private const string MyBookingUrl = "https://ehall.szu.edu.cn/qljfwapp/sys/lwSzuCgyy/index.do#/myBooking";
    private const string NoSecondaryTime = "不启用";
    private static readonly string[] TimeSlots = Enumerable.Range(8, 14).Select(hour => $"{hour:00}:00-{hour + 1:00}:00").ToArray();
    private static readonly JsonSerializerOptions ScriptJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ProxyProcessService _proxyService = new();
    private readonly DispatcherTimer _scheduleTimer = new();
    private AppSettings _settings;
    private CancellationTokenSource? _bookingCancellation;
    private TaskCompletionSource<bool>? _navigationCompletion;
    private bool _browserReady;
    private bool _initializing = true;
    private bool _accountSelectionChanging;
    private bool _scheduleTickBusy;
    private DateTime _lastScheduleDate = DateTime.MinValue;
    private bool _isReleaseMonitoring;
    private bool _courtOptionsLoading;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        if (_settings.ScheduledReleaseBooking is { } scheduledBooking
            && scheduledBooking.RunAt.TimeOfDay == BookingSchedule.ReleaseTime)
        {
            scheduledBooking.RunAt = scheduledBooking.RunAt.Date + BookingSchedule.BookingStartTime;
            SettingsService.Save(_settings);
        }
        TimeComboBox.ItemsSource = TimeSlots;
        SecondaryTimeComboBox.ItemsSource = new[] { NoSecondaryTime }.Concat(TimeSlots).ToArray();
        ApplySettingsToControls();
        RefreshAccountList();
        _proxyService.OutputReceived += message => Dispatcher.Invoke(() =>
        {
            AppendLog(message);
            UpdateProxyStatus();
        });
        _scheduleTimer.Interval = TimeSpan.FromSeconds(1);
        _scheduleTimer.Tick += ScheduleTimer_Tick;
        _scheduleTimer.Start();
        UpdateScheduleStatus();
        Loaded += async (_, _) => await InitializeBrowserAsync();
        Closed += MainWindow_Closed;
        _initializing = false;
    }

    private void ApplySettingsToControls()
    {
        SelectComboText(CampusComboBox, _settings.Campus);
        TimeComboBox.SelectedItem = TimeSlots.Contains(_settings.PreferredTime) ? _settings.PreferredTime : TimeSlots[^3];
        SecondaryTimeComboBox.SelectedItem = _settings.SecondaryPreferredTime == NoSecondaryTime || TimeSlots.Contains(_settings.SecondaryPreferredTime)
            ? _settings.SecondaryPreferredTime
            : NoSecondaryTime;
        CourtPreferenceComboBox.Text = string.IsNullOrWhiteSpace(_settings.CourtPreference)
            ? CourtPreference.AllCourts
            : _settings.CourtPreference;
        RefreshTextBox.Text = _settings.RefreshSeconds.ToString();
        AutoLoginCheckBox.IsChecked = _settings.AutoLogin;
        AutoOpenCheckBox.IsChecked = _settings.AutoOpenBadminton;
        DailyScheduleCheckBox.IsChecked = _settings.DailyAutoBooking;
        UseProxyCheckBox.IsChecked = _settings.UseBrowserProxy;
        SelectComboText(ProxySchemeComboBox, _settings.ProxyScheme);
        ProxyHostTextBox.Text = _settings.ProxyHost;
        ProxyPortTextBox.Text = _settings.ProxyPort.ToString();
        ProxyPathTextBox.Text = _settings.ProxyExecutablePath;
        ProxyArgumentsTextBox.Text = _settings.ProxyArguments;
        StopProxyOnExitCheckBox.IsChecked = _settings.StopProxyOnExit;
        UpdateCourtOptions();
    }

    private void CampusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        CourtPreferenceComboBox.Text = CourtPreference.AllCourts;
        UpdateCourtOptions();
    }

    private async void CourtPreferenceComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (!_browserReady || _courtOptionsLoading)
        {
            return;
        }

        _courtOptionsLoading = true;
        try
        {
            var courtNames = await QueryCourtNamesAsync();
            UpdateCourtOptions(courtNames);
            SelectionStatusText.Text = $"已读取 {courtNames.Count} 个学校真实场号，可直接选择具体场地。";
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = "真实场号读取失败：" + exception.Message + "；仍可选择场馆大类或手动输入。";
        }
        finally
        {
            _courtOptionsLoading = false;
        }
    }

    private void UpdateCourtOptions(IEnumerable<string>? liveCourtNames = null)
    {
        var preference = GetCourtPreference();
        CourtPreferenceComboBox.ItemsSource = CourtPreference.GetOptions(GetComboText(CampusComboBox), liveCourtNames);
        CourtPreferenceComboBox.Text = preference.Length == 0 ? CourtPreference.AllCourts : preference;
    }

    private string GetCourtPreference() => CourtPreference.Normalize(CourtPreferenceComboBox.Text);

    private async Task<IReadOnlyList<string>> QueryCourtNamesAsync()
    {
        if (WebView.Source is null || !WebView.Source.AbsoluteUri.Contains("/lwSzuCgyy/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("请先进入学校预约页面");
        }

        var campusCode = GetComboText(CampusComboBox).Contains("丽湖", StringComparison.Ordinal) ? "2" : "1";
        var date = DateComboBox.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
        {
            date = VenueDateRules.GetDefaultTargetDate(DateTime.Now);
        }
        var time = TimeSlots.Contains(TimeComboBox.Text) ? TimeComboBox.Text : TimeSlots[0];
        var script = $$"""
            (async () => {
                try {
                    const [startTime, endTime] = {{ToJs(time)}}.split('-');
                    const body = new URLSearchParams({
                        XMDM: '001', YYRQ: {{ToJs(date)}}, YYLX: '1.0',
                        KSSJ: startTime, JSSJ: endTime, XQDM: {{ToJs(campusCode)}}
                    });
                    const response = await fetch('/qljfwapp/sys/lwSzuCgyy/modules/sportVenue/getOpeningRoom.do', {
                        method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' },
                        body, credentials: 'same-origin', cache: 'no-cache'
                    });
                    if (!response.ok) return { ok: false, message: '学校接口 HTTP ' + response.status + '，请重新登录' };
                    const data = await response.json();
                    if (String(data.code) !== '0') return { ok: false, message: data.msg || '学校未返回场号' };
                    const rows = data.datas?.getOpeningRoom?.rows || [];
                    const names = rows.map(room => String(room.CGBM_DISPLAY || room.CDMC || '').trim()).filter(Boolean);
                    return { ok: true, names: [...new Set(names)] };
                } catch (error) {
                    return { ok: false, message: error.message || '场号查询异常' };
                }
            })()
            """;
        var json = await RunAsyncScriptAsync(script);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : null;
            throw new InvalidOperationException(message ?? "学校未返回场号");
        }

        return root.GetProperty("names").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private void RefreshAccountList()
    {
        _accountSelectionChanging = true;
        var selected = _settings.Accounts.FirstOrDefault(account => account.Id == _settings.SelectedAccountId)
            ?? _settings.Accounts.FirstOrDefault();
        AccountsComboBox.ItemsSource = null;
        AccountsComboBox.ItemsSource = _settings.Accounts;
        AccountsComboBox.SelectedItem = selected;
        _settings.SelectedAccountId = selected?.Id ?? string.Empty;
        StudentIdTextBox.Text = selected?.StudentId ?? string.Empty;
        AccountPasswordBox.Clear();
        _accountSelectionChanging = false;
    }

    private SavedAccount? SelectedAccount => AccountsComboBox.SelectedItem as SavedAccount;

    private void AccountsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_accountSelectionChanging)
        {
            return;
        }

        var account = SelectedAccount;
        _settings.SelectedAccountId = account?.Id ?? string.Empty;
        StudentIdTextBox.Text = account?.StudentId ?? string.Empty;
        AccountPasswordBox.Clear();
        SettingsService.Save(_settings);
        SelectionStatusText.Text = account is null ? "请先保存校园账号。" : "已选择账号，将在下次登录时使用。";
    }

    private void SaveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var studentId = StudentIdTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(studentId))
            {
                throw new InvalidOperationException("请输入学号。");
            }

            var account = _settings.Accounts.FirstOrDefault(item => item.StudentId == studentId);
            var password = AccountPasswordBox.Password;
            if (account is null)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("新账号需要输入密码。");
                }

                account = new SavedAccount();
                _settings.Accounts.Add(account);
            }

            account.StudentId = studentId;
            if (!string.IsNullOrEmpty(password))
            {
                account.EncryptedPassword = CredentialProtector.Protect(password);
            }
            else if (string.IsNullOrWhiteSpace(account.EncryptedPassword))
            {
                throw new InvalidOperationException("请输入密码后保存。");
            }

            _settings.SelectedAccountId = account.Id;
            SettingsService.Save(_settings);
            RefreshAccountList();
            SelectionStatusText.Text = "账号已加密保存，下次启动将自动使用该账号登录。";
            AppendLog("校园账号已保存；密码仅以 Windows 当前用户密文存储。");
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = exception.Message;
        }
        finally
        {
            AccountPasswordBox.Clear();
        }
    }

    private void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account is null)
        {
            return;
        }

        if (MessageBox.Show(this, "确定删除所选学号及其已加密密码吗？", "删除账号", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Accounts.Remove(account);
        _settings.SelectedAccountId = _settings.Accounts.FirstOrDefault()?.Id ?? string.Empty;
        SettingsService.Save(_settings);
        RefreshAccountList();
        SelectionStatusText.Text = "账号已删除。";
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var userDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SzuBadminton",
                "WebView2");
            Directory.CreateDirectory(userDataDirectory);

            var options = new CoreWebView2EnvironmentOptions();
            if (_settings.UseBrowserProxy)
            {
                options.AdditionalBrowserArguments = $"--proxy-server={_settings.ProxyScheme}://{_settings.ProxyHost}:{_settings.ProxyPort}";
                AppendLog($"浏览器代理已配置为 {_settings.ProxyScheme}://{_settings.ProxyHost}:{_settings.ProxyPort}。");
            }

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataDirectory, options);
            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.NavigationStarting += (_, _) =>
            {
                LoadingPanel.Visibility = Visibility.Visible;
                BrowserStatusText.Text = "页面加载中";
            };
            WebView.CoreWebView2.NavigationCompleted += (_, eventArgs) =>
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                BrowserStatusText.Text = eventArgs.IsSuccess ? "页面已就绪" : "页面加载失败";
                _navigationCompletion?.TrySetResult(eventArgs.IsSuccess);
            };
            WebView.CoreWebView2.NewWindowRequested += (_, eventArgs) =>
            {
                eventArgs.Handled = true;
                WebView.CoreWebView2.Navigate(eventArgs.Uri);
            };
            _browserReady = true;
            AppendLog("浏览器初始化完成，准备自动登录。");
            await NavigateAsync(VenueUrl);
            await AutoLoginAndOpenAsync(_settings.AutoOpenBadminton);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            BrowserStatusText.Text = "缺少 WebView2 运行时";
            MessageBox.Show("未检测到 Microsoft Edge WebView2 运行时，请安装常青版运行时后重新打开程序。", "需要 WebView2", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            BrowserStatusText.Text = "初始化失败";
            AppendLog("浏览器初始化失败：" + exception.Message);
        }
    }

    private async Task NavigateAsync(string url)
    {
        if (!_browserReady)
        {
            return;
        }

        _navigationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        WebView.CoreWebView2.Navigate(url);
        var completed = await Task.WhenAny(_navigationCompletion.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        if (completed != _navigationCompletion.Task)
        {
            throw new TimeoutException("页面加载超时，请检查网络或代理。");
        }
    }

    private async void VenueButton_Click(object sender, RoutedEventArgs e) => await NavigateAsync(VenueUrl);
    private async void MyBookingButton_Click(object sender, RoutedEventArgs e) => await NavigateAsync(MyBookingUrl);

    private async void LoginNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureBrowserReady())
        {
            return;
        }

        try
        {
            SaveSettingsFromControls(false);
            LoginNowButton.IsEnabled = false;
            await AutoLoginAndOpenAsync(true);
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = exception.Message;
        }
        finally
        {
            LoginNowButton.IsEnabled = true;
        }
    }

    private async Task<bool> AutoLoginAndOpenAsync(bool openBookingForm)
    {
        if (IsAuthPage())
        {
            if (!_settings.AutoLogin)
            {
                SelectionStatusText.Text = "自动登录未启用，请在右侧手动登录。";
                return false;
            }

            if (!await AutoLoginAsync())
            {
                return false;
            }
        }

        if (WebView.Source is null || !WebView.Source.AbsoluteUri.Contains("/lwSzuCgyy/", StringComparison.OrdinalIgnoreCase))
        {
            await NavigateAsync(VenueUrl);
        }

        if (IsAuthPage())
        {
            return false;
        }

        if (openBookingForm)
        {
            return await OpenBadmintonAsync();
        }

        SelectionStatusText.Text = "已登录，每日 12:30 将自动执行抢场。";
        return true;
    }

    private async Task<bool> AutoLoginAsync()
    {
        var account = SelectedAccount
            ?? _settings.Accounts.FirstOrDefault(item => item.Id == _settings.SelectedAccountId);
        if (account is null)
        {
            SelectionStatusText.Text = "尚未保存校园账号，请先在左侧保存学号和密码。";
            return false;
        }

        try
        {
            SelectionStatusText.Text = "正在使用已选账号自动登录……";
            var password = CredentialProtector.Unprotect(account.EncryptedPassword);
            var script = $$"""
                (() => {
                    const form = document.querySelector('#pwdFromId');
                    if (!form) return { ok: false, message: '登录表单尚未加载，请稍后重试。' };
                    const captcha = form.querySelector('#captchaDiv');
                    if (captcha && !captcha.classList.contains('hide') && captcha.getBoundingClientRect().height > 0) {
                        return { ok: false, captcha: true, message: '学校要求图形验证码，请在右侧完成验证后登录。' };
                    }
                    const username = form.querySelector('#username');
                    const passwordInput = form.querySelector('#password');
                    const submit = form.querySelector('#login_submit');
                    if (!username || !passwordInput || !submit) return { ok: false, message: '未识别到学号密码登录控件。' };
                    passwordInput.removeAttribute('readonly');
                    const setValue = (element, value) => {
                        element.focus();
                        element.value = value;
                        element.dispatchEvent(new Event('input', { bubbles: true }));
                        element.dispatchEvent(new Event('change', { bubbles: true }));
                        element.dispatchEvent(new Event('blur', { bubbles: true }));
                    };
                    setValue(username, {{ToJs(account.StudentId)}});
                    setValue(passwordInput, {{ToJs(password)}});
                    const remember = form.querySelector('#rememberMe');
                    if (remember) remember.checked = true;
                    submit.click();
                    return { ok: true };
                })()
                """;
            var result = await RunScriptAsync(script);
            if (!ReadOk(result))
            {
                SelectionStatusText.Text = ReadMessage(result, "自动登录失败。");
                AppendLog(SelectionStatusText.Text);
                return false;
            }

            for (var attempt = 0; attempt < 50; attempt++)
            {
                await Task.Delay(400);
                if (!IsAuthPage())
                {
                    AppendLog("学校统一身份认证通过。");
                    SelectionStatusText.Text = "登录成功，正在进入羽毛球预约窗口。";
                    return true;
                }

                var state = await RunScriptAsync("""
                    (() => {
                        const form = document.querySelector('#pwdFromId');
                        const captcha = form?.querySelector('#captchaDiv');
                        const error = form?.querySelector('#showErrorTip')?.textContent.trim() || '';
                        return {
                            captcha: !!(captcha && !captcha.classList.contains('hide') && captcha.getBoundingClientRect().height > 0),
                            error
                        };
                    })()
                    """);
                if (ReadBoolean(state, "captcha"))
                {
                    SelectionStatusText.Text = "学校要求验证码，请在右侧完成验证后登录。";
                    AppendLog(SelectionStatusText.Text);
                    return false;
                }

                var error = ReadString(state, "error");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    SelectionStatusText.Text = "自动登录未通过，请检查学号、密码或学校验证要求。";
                    AppendLog(SelectionStatusText.Text);
                    return false;
                }
            }

            SelectionStatusText.Text = "登录等待超时，请检查网络、账号状态或验证码。";
            return false;
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = "自动登录失败：" + exception.Message;
            AppendLog(SelectionStatusText.Text);
            return false;
        }
    }

    private bool IsAuthPage() => WebView.Source?.Host.Contains("authserver.szu.edu.cn", StringComparison.OrdinalIgnoreCase) == true;

    private async void OpenBadmintonButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureBrowserReady())
        {
            return;
        }

        try
        {
            OpenBadmintonButton.IsEnabled = false;
            await OpenBadmintonAsync();
        }
        finally
        {
            OpenBadmintonButton.IsEnabled = true;
        }
    }

    private async Task<bool> OpenBadmintonAsync()
    {
        try
        {
            if (IsAuthPage())
            {
                SelectionStatusText.Text = "尚未登录，请先保存账号并自动登录。";
                return false;
            }

            if (WebView.Source is null
                || !WebView.Source.AbsoluteUri.Contains("/lwSzuCgyy/", StringComparison.OrdinalIgnoreCase)
                || !WebView.Source.AbsoluteUri.Contains("#/sportVenue", StringComparison.OrdinalIgnoreCase))
            {
                await NavigateAsync(VenueUrl);
            }

            await RunScriptAsync("""
                (() => {
                    const cancel = [...document.querySelectorAll('#apply button')].find(item => item.textContent.trim() === '取消');
                    if (cancel) cancel.click();
                    return true;
                })()
                """);
            await Task.Delay(180);

            var campus = GetComboText(CampusComboBox);
            if (!await WaitForVisibleTextAsync(campus))
            {
                SelectionStatusText.Text = "校区入口加载超时，请检查网络后重试。";
                return false;
            }

            var campusResult = await RunScriptAsync($$"""
                (() => {
                    const campus = {{ToJs(campus)}};
                    const visible = element => {
                        const rect = element.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    };
                    const target = [...document.querySelectorAll('div, a, span')]
                        .find(element => element.children.length === 0 && element.textContent.trim() === campus && visible(element));
                    if (!target) return { ok: false, message: '未找到校区入口。' };
                    target.click();
                    return { ok: true };
                })()
                """);
            if (!ReadOk(campusResult))
            {
                SelectionStatusText.Text = ReadMessage(campusResult, "未能选择校区。");
                return false;
            }

            if (!await WaitForVisibleTextAsync("羽毛球"))
            {
                SelectionStatusText.Text = "羽毛球入口加载超时，请检查网络后重试。";
                return false;
            }

            var sportResult = await RunScriptAsync("""
                (() => {
                    const visible = element => {
                        const rect = element.getBoundingClientRect();
                        return rect.width > 0 && rect.height > 0;
                    };
                    const target = [...document.querySelectorAll('div, a, span')]
                        .find(element => element.children.length === 0 && element.textContent.trim() === '羽毛球' && visible(element));
                    if (!target) return { ok: false, message: '未找到羽毛球入口。' };
                    target.click();
                    return { ok: true };
                })()
                """);
            if (!ReadOk(sportResult))
            {
                SelectionStatusText.Text = ReadMessage(sportResult, "未能打开羽毛球预约窗口。");
                return false;
            }

            if (!await WaitForScriptAsync("!!document.querySelector('#apply')", TimeSpan.FromSeconds(12)))
            {
                SelectionStatusText.Text = "预约窗口加载超时，尚未读取到预约表单。";
                return false;
            }

            var dates = ApplyRuleBasedDates();
            SelectionStatusText.Text = "预约窗口已打开，已按时间规则确定日期：" + string.Join("、", dates) + "。";
            return true;
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = "打开预约窗口失败：" + exception.Message;
            AppendLog(SelectionStatusText.Text);
            return false;
        }
    }

    private async Task<bool> WaitForVisibleTextAsync(string text)
    {
        var script = $$"""
            (() => {
                const text = {{ToJs(text)}};
                return [...document.querySelectorAll('div, a, span')].some(element => {
                    const rect = element.getBoundingClientRect();
                    return element.children.length === 0 && element.textContent.trim() === text && rect.width > 0 && rect.height > 0;
                });
            })()
            """;
        return await WaitForScriptAsync(script, TimeSpan.FromSeconds(12));
    }

    private async Task<bool> WaitForScriptAsync(string script, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await RunScriptAsync(script) == "true")
                {
                    return true;
                }
            }
            catch
            {
                // 页面跳转期间脚本可能暂时无法执行，继续等待到超时。
            }
            await Task.Delay(250);
        }
        return false;
    }

    private List<string> ApplyRuleBasedDates()
    {
        var dates = VenueDateRules.GetExpectedDates(DateTime.Now).ToList();
        var selectedDate = DateComboBox.SelectedItem as string;
        var defaultTarget = VenueDateRules.GetDefaultTargetDate(DateTime.Now);
        DateComboBox.ItemsSource = dates;
        DateComboBox.SelectedItem = selectedDate is not null && dates.Contains(selectedDate)
            ? selectedDate
            : dates.Contains(defaultTarget) ? defaultTarget : dates.LastOrDefault();
        return dates;
    }

    private async void BookNowButton_Click(object sender, RoutedEventArgs e) =>
        await StartBookingFromControlsAsync(null, false);

    private async void MonitorNowButton_Click(object sender, RoutedEventArgs e) =>
        await StartBookingFromControlsAsync(BookingSchedule.GetTargetDate(DateTime.Now), true);

    private async Task StartBookingFromControlsAsync(string? requiredDate, bool isReleaseMonitor)
    {
        if (!EnsureBrowserReady() || _bookingCancellation is not null)
        {
            return;
        }

        try
        {
            SaveSettingsFromControls(false);
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = exception.Message;
            return;
        }

        if (requiredDate is null && !isReleaseMonitor)
        {
            var selectedDate = DateComboBox.SelectedItem as string ?? VenueDateRules.GetDefaultTargetDate(DateTime.Now);
            if (!BookingSchedule.IsTargetReleased(DateTime.Now, selectedDate))
            {
                SelectionStatusText.Text = $"{selectedDate} 尚未放场（{BookingSchedule.GetReleaseAt(selectedDate):yyyy-MM-dd HH:mm} 放场），请设置为 12:29 监测抢场计划。";
                return;
            }
        }

        await RunBookingLoopAsync(requiredDate, isReleaseMonitor);
    }

    private async Task RunBookingLoopAsync(string? requiredDate, bool isReleaseMonitor = false)
    {
        if (_bookingCancellation is not null)
        {
            return;
        }

        _bookingCancellation = new CancellationTokenSource();
        _isReleaseMonitoring = isReleaseMonitor;
        var token = _bookingCancellation.Token;
        var tracker = new BookingSlotTracker(GetPreferredTimes());
        BookNowButton.IsEnabled = false;
        MonitorNowButton.IsEnabled = false;
        StopBookingButton.IsEnabled = true;
        if (isReleaseMonitor)
        {
            AppendLog($"开始 {requiredDate} 次日放场监测；将持续查询，发现可订场地立即提交。");
        }
        else
        {
            AppendLog(requiredDate is null
                ? "开始自动抢场；每个时段成功一场后停止，其余时段持续重试。"
                : $"开始 {requiredDate} 放场任务；每个时段独立抢场，直到全部成功或手动停止。");
        }

        try
        {
            if (IsAuthPage() && !await AutoLoginAsync())
            {
                return;
            }

            while (!token.IsCancellationRequested && !tracker.AllBooked)
            {
                if (!await OpenBadmintonAsync())
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RefreshSeconds), token);
                    continue;
                }

                var dates = DateComboBox.Items.Cast<string>().ToList();
                var selectedDate = DateComboBox.SelectedItem as string;
                var targetDate = requiredDate ?? (selectedDate is not null && dates.Contains(selectedDate)
                    ? selectedDate
                    : VenueDateRules.GetDefaultTargetDate(DateTime.Now));
                if (isReleaseMonitor && !dates.Contains(targetDate))
                {
                    dates.Add(targetDate);
                    dates.Sort(StringComparer.Ordinal);
                    DateComboBox.ItemsSource = dates;
                }
                else if (!dates.Contains(targetDate))
                {
                    SelectionStatusText.Text = $"目标日期 {targetDate} 不符合当前放场时间规则，正在等待……";
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RefreshSeconds), token);
                    continue;
                }

                DateComboBox.SelectedItem = targetDate;
                foreach (var time in tracker.Pending.ToList())
                {
                    token.ThrowIfCancellationRequested();
                    SelectionStatusText.Text = isReleaseMonitor
                        ? $"{targetDate} {time}：正在监测场地余量……"
                        : $"{targetDate} {time}：正在通过学校接口查询并提交……";
                    var result = await TryDirectBookingAsync(targetDate, time);
                    if (!result.Success)
                    {
                        SelectionStatusText.Text = isReleaseMonitor
                            ? $"{targetDate} {time}：监测中，{result.Message}；{_settings.RefreshSeconds} 秒后重试。"
                            : $"{targetDate} {time}：{result.Message}；等待下次重试。";
                        continue;
                    }

                    tracker.MarkBooked(time);
                    SelectionStatusText.Text = tracker.AllBooked
                        ? $"全部候选时段均已完成。最后完成：{targetDate} {time} {result.Court}。"
                        : $"{time} 已完成并停止该时段；继续抢 {string.Join("、", tracker.Pending)}。";
                    AppendLog(result.AlreadyBooked
                        ? $"学校确认该时段已经预约：{targetDate} {time}，按已完成处理。"
                        : $"预约成功：{targetDate} {time} {result.Court}。{result.Message}");
                    if (tracker.AllBooked)
                    {
                        Activate();
                        Topmost = true;
                        Topmost = false;
                        return;
                    }

                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.RefreshSeconds), token);
            }
        }
        catch (OperationCanceledException)
        {
            SelectionStatusText.Text = tracker.AllBooked
                ? "全部候选时段已完成。"
                : $"已手动停止；仍未完成的时段：{string.Join("、", tracker.Pending)}。";
            AppendLog(SelectionStatusText.Text);
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = "自动抢场中断：" + exception.Message;
            AppendLog(SelectionStatusText.Text);
        }
        finally
        {
            _bookingCancellation?.Dispose();
            _bookingCancellation = null;
            _isReleaseMonitoring = false;
            BookNowButton.IsEnabled = true;
            MonitorNowButton.IsEnabled = true;
            StopBookingButton.IsEnabled = false;
            ApplyRuleBasedDates();
            UpdateScheduleStatus();
        }
    }

    private List<string> GetPreferredTimes()
    {
        var primary = TimeComboBox.Text.Trim();
        var secondary = SecondaryTimeComboBox.Text.Trim();
        return new[] { primary, secondary }
            .Where(time => TimeSlots.Contains(time))
            .Distinct()
            .Take(2)
            .ToList();
    }

    private async Task<DirectBookingResult> TryDirectBookingAsync(string date, string time)
    {
        var campusCode = GetComboText(CampusComboBox).Contains("丽湖", StringComparison.Ordinal) ? "2" : "1";
        var preference = GetCourtPreference();
        var script = $$"""
            (async () => {
                try {
                    const date = {{ToJs(date)}};
                    const timeSlot = {{ToJs(time)}};
                    const preference = {{ToJs(preference)}};
                    const [startTime, endTime] = timeSlot.split('-');
                    const headers = { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8' };
                    const base = '/qljfwapp/sys/lwSzuCgyy';
                    const queryPayload = new URLSearchParams({
                        XMDM: '001', YYRQ: date, YYLX: '1.0',
                        KSSJ: startTime, JSSJ: endTime, XQDM: {{ToJs(campusCode)}}
                    });
                    const queryResponse = await fetch(base + '/modules/sportVenue/getOpeningRoom.do', {
                        method: 'POST', headers, body: queryPayload,
                        credentials: 'same-origin', mode: 'cors', cache: 'no-cache'
                    });
                    if (!queryResponse.ok) return { success: false, message: '场地查询 HTTP ' + queryResponse.status };
                    const queryData = await queryResponse.json();
                    if (String(queryData.code) !== '0') return { success: false, message: queryData.msg || '场地查询失败' };
                    const rooms = queryData.datas?.getOpeningRoom?.rows || [];
                    const available = rooms.filter(room => {
                        if (room.disabled === true || room.disabled === 'true') return false;
                        const text = String(room.text || '').trim();
                        if (text === '可预约') return true;
                        const match = text.match(/^(\d+)\/(\d+)$/);
                        return match ? Number(match[1]) > 0 && Number(match[1]) <= Number(match[2]) : false;
                    });
                    const courtName = room => String(room.CGBM_DISPLAY || room.CDMC || '').trim();
                    const courtPriority = room => {
                        const name = `${courtName(room)} ${String(room.CDMC || '')}`;
                        if ({{ToJs(campusCode)}} === '2') {
                            const match = name.match(/(\d{1,2})\s*号场/);
                            return match
                                ? { recognized: 0, zone: 0, number: -Number(match[1]) }
                                : { recognized: 1, zone: 0, number: 0 };
                        }
                        const match = name.match(/([ABCD])\s*(?:区|馆)?\s*[-—_]?\s*(\d{1,2})/i);
                        const zones = { D: 0, C: 1, B: 2, A: 3 };
                        return match
                            ? { recognized: 0, zone: zones[match[1].toUpperCase()], number: Number(match[2]) }
                            : { recognized: 1, zone: 0, number: 0 };
                    };
                    const orderedRooms = available
                        .map((room, index) => ({ room, index, priority: courtPriority(room) }))
                        .sort((left, right) =>
                            left.priority.recognized - right.priority.recognized ||
                            left.priority.zone - right.priority.zone ||
                            left.priority.number - right.priority.number ||
                            courtName(left.room).localeCompare(courtName(right.room), 'zh-CN', { numeric: true }) ||
                            left.index - right.index)
                        .map(item => item.room);
                    const room = preference
                        ? orderedRooms.find(item => courtName(item).includes(preference) || String(item.CDMC || '').includes(preference))
                        : orderedRooms[0];
                    if (!room) return { success: false, message: preference ? '没有符合偏好的可用场地' : '暂时没有可用场地' };

                    const userId = typeof USERID !== 'undefined' ? USERID : '';
                    const userName = typeof USER_INFO !== 'undefined' ? USER_INFO.userName : '';
                    if (!userId || !userName) return { success: false, message: '登录人信息不完整，请重新登录' };
                    const court = String(room.CGBM_DISPLAY || room.CDMC || '可用场地');
                    const payload = new URLSearchParams({
                        DHID: '', YYRGH: userId, CYRS: '', YYRXM: userName,
                        CQBM: room.CQBM || '', CGDM: room.CGBM || '', CDWID: room.WID || '',
                        XMDM: room.XMDM || '001', XQWID: room.XQDM || {{ToJs(campusCode)}},
                        KYYSJD: timeSlot, YYRQ: date, YYLX: '1.0',
                        YYKS: date + ' ' + startTime, YYJS: date + ' ' + endTime,
                        PC_OR_PHONE: 'pc'
                    });
                    if (!payload.get('CGDM') || !payload.get('CDWID')) {
                        return { success: false, message: '场地参数不完整，未提交' };
                    }

                    const response = await fetch(base + '/sportVenue/insertVenueBookingInfo.do', {
                        method: 'POST', headers, body: payload,
                        credentials: 'same-origin', mode: 'cors', cache: 'no-cache'
                    });
                    if (!response.ok) return { success: false, message: '预约提交 HTTP ' + response.status };
                    const contentType = response.headers.get('content-type') || '';
                    if (contentType.includes('text/html')) {
                        const html = await response.text();
                        const match = html.match(/<h4>出错信息：<\/h4>[\s\S]*?<div class="bh-text-caption bh-color-caption">\s*(.*?)\s*<\/div>/);
                        const message = match ? match[1].trim() : '学校系统返回异常页面';
                        if (message.includes('已预约')) return { success: true, alreadyBooked: true, court, message };
                        return { success: false, court, message };
                    }

                    const result = await response.json();
                    const message = result.msg || '';
                    if (String(result.code) === '0') return { success: true, court, message };
                    if (message.includes('已预约')) return { success: true, alreadyBooked: true, court, message };
                    return { success: false, court, message: message || '学校拒绝预约' };
                } catch (error) {
                    return { success: false, message: error.message || '预约请求异常' };
                }
            })()
            """;
        var json = await RunAsyncScriptAsync(script);
        return DirectBookingResult.Parse(json);
    }

    private async Task<BookingSelectionResult> FindAndSelectCourtAsync(string date, string time)
    {
        var formExists = await RunScriptAsync("!!document.querySelector('#apply')");
        if (formExists != "true")
        {
            return BookingSelectionResult.Failure("预约窗口已关闭，正在重新打开。");
        }

        var dateResult = await RunScriptAsync($$"""
            (() => {
                const label = [...document.querySelectorAll('#apply label')].find(item => item.htmlFor === {{ToJs(date)}});
                if (!label) return { ok: false, message: '该日期不在当前可预约日期中。' };
                label.click();
                return { ok: true };
            })()
            """);
        if (!ReadOk(dateResult))
        {
            return BookingSelectionResult.Failure(ReadMessage(dateResult, "日期选择失败。"));
        }

        await Task.Delay(160);
        var timeResult = await RunScriptAsync($$"""
            (() => {
                const label = [...document.querySelectorAll('#apply label')].find(item => item.htmlFor === {{ToJs(time)}});
                if (!label) return { ok: false, message: '未找到该预约时段。' };
                const available = !!label.querySelector('.frame-child1') || !!label.querySelector('.rectangle-wrapper');
                if (!available) return { ok: false, message: '该时段当前不可预约。' };
                label.click();
                return { ok: true };
            })()
            """);
        if (!ReadOk(timeResult))
        {
            return BookingSelectionResult.Failure(ReadMessage(timeResult, "该时段当前不可预约。"));
        }

        await Task.Delay(240);
        var preference = GetCourtPreference();
        var courtResult = await RunScriptAsync($$"""
            (() => {
                const preference = {{ToJs(preference)}};
                const labels = [...document.querySelectorAll('#apply label')]
                    .filter(label => /^[0-9a-f]{32}$/i.test(label.htmlFor));
                const available = labels.filter(label => !!label.querySelector('.frame-child1') || !!label.querySelector('.rectangle-wrapper'));
                const target = preference
                    ? available.find(label => label.textContent.includes(preference))
                    : available[0];
                if (!target) return { ok: false, message: preference ? '没有符合偏好的可用场地。' : '暂时没有可用场地。' };
                target.click();
                return { ok: true, court: target.textContent.trim() };
            })()
            """);
        return ReadOk(courtResult)
            ? BookingSelectionResult.Selected(ReadCourt(courtResult))
            : BookingSelectionResult.Failure(ReadMessage(courtResult, "没有可用场地。"));
    }

    private async Task<bool> SubmitSelectionAsync()
    {
        var clicked = await RunScriptAsync("""
            (() => {
                const button = [...document.querySelectorAll('#apply button')].find(item => item.textContent.trim() === '提交预约');
                if (!button) return false;
                window.__szuBookingResponse = null;
                if (!window.__szuBookingHookInstalled) {
                    const originalSend = XMLHttpRequest.prototype.send;
                    XMLHttpRequest.prototype.send = function(...args) {
                        this.addEventListener('load', () => {
                            if ((this.responseURL || '').includes('/sportVenue/insertVenueBookingInfo.do')) {
                                window.__szuBookingResponse = this.responseText;
                            }
                        });
                        return originalSend.apply(this, args);
                    };
                    window.__szuBookingHookInstalled = true;
                }
                button.click();
                return true;
            })()
            """);
        if (clicked != "true")
        {
            AppendLog("未找到提交预约按钮。");
            return false;
        }

        var token = _bookingCancellation?.Token ?? CancellationToken.None;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(200, token);
            var rawResponse = await RunScriptAsync("window.__szuBookingResponse || null");
            if (rawResponse != "null")
            {
                string responseText;
                using (var document = JsonDocument.Parse(rawResponse))
                {
                    responseText = document.RootElement.ValueKind == JsonValueKind.String
                        ? document.RootElement.GetString() ?? string.Empty
                        : document.RootElement.GetRawText();
                }

                var response = BookingResponseParser.Parse(responseText);
                if (!response.Success)
                {
                    AppendLog(string.IsNullOrWhiteSpace(response.Message)
                        ? "学校拒绝了本次预约，未返回具体原因。"
                        : "学校返回：" + response.Message);
                }
                return response.Success;
            }

            if (WebView.Source?.AbsoluteUri.Contains("#/myBooking", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        AppendLog("10 秒内未收到学校明确的预约结果，将继续重试。");
        return false;
    }

    private void StopBookingButton_Click(object sender, RoutedEventArgs e) => _bookingCancellation?.Cancel();

    private void ScheduleReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettingsFromControls(false);
            var runAt = BookingSchedule.GetNextBookingStart(DateTime.Now);
            var targetDate = BookingSchedule.GetTargetDate(runAt);
            var selectedDate = DateComboBox.SelectedItem as string ?? DateComboBox.Text.Trim();
            if (selectedDate != targetDate)
            {
                throw new InvalidOperationException($"所选 {selectedDate} 已放场或不在下一放场批次；抢场计划请选择尚未放场的 {targetDate}。");
            }

            _settings.ScheduledReleaseBooking = new ReleaseBookingTask
            {
                RunAt = runAt,
                TargetDate = targetDate,
                Campus = GetComboText(CampusComboBox),
                PreferredTime = TimeComboBox.Text.Trim(),
                SecondaryPreferredTime = SecondaryTimeComboBox.Text.Trim(),
                CourtPreference = GetCourtPreference(),
                RefreshSeconds = _settings.RefreshSeconds
            };
            SettingsService.Save(_settings);
            UpdateScheduleStatus();
            SelectionStatusText.Text = $"已设置 {runAt:yyyy-MM-dd HH:mm} 自动抢 {BookingSchedule.GetTargetDate(runAt)} 的当前选择。";
            AppendLog(SelectionStatusText.Text);
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = exception.Message;
        }
    }

    private void CancelScheduledReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ScheduledReleaseBooking = null;
        SettingsService.Save(_settings);
        UpdateScheduleStatus();
        SelectionStatusText.Text = "一次性 12:29 监测抢场计划已取消。";
    }

    private void ApplyScheduledReleaseBooking(ReleaseBookingTask task)
    {
        SelectComboText(CampusComboBox, task.Campus);
        TimeComboBox.SelectedItem = TimeSlots.Contains(task.PreferredTime) ? task.PreferredTime : TimeSlots[^3];
        SecondaryTimeComboBox.SelectedItem = task.SecondaryPreferredTime == NoSecondaryTime || TimeSlots.Contains(task.SecondaryPreferredTime)
            ? task.SecondaryPreferredTime
            : NoSecondaryTime;
        CourtPreferenceComboBox.Text = string.IsNullOrWhiteSpace(task.CourtPreference)
            ? CourtPreference.AllCourts
            : task.CourtPreference;
        RefreshTextBox.Text = Math.Clamp(task.RefreshSeconds, 3, 60).ToString();
        UpdateCourtOptions();

        _settings.Campus = GetComboText(CampusComboBox);
        _settings.PreferredTime = TimeComboBox.Text.Trim();
        _settings.SecondaryPreferredTime = SecondaryTimeComboBox.Text.Trim();
        _settings.CourtPreference = GetCourtPreference();
        _settings.RefreshSeconds = Math.Clamp(task.RefreshSeconds, 3, 60);
    }

    private async void ScheduleTimer_Tick(object? sender, EventArgs e)
    {
        UpdateScheduleStatus();
        if (_scheduleTickBusy || !_browserReady || _bookingCancellation is not null)
        {
            return;
        }

        var now = DateTime.Now;
        var scheduledBooking = _settings.ScheduledReleaseBooking;
        if (scheduledBooking is not null
            && BookingSchedule.IsWithinRunWindow(now, scheduledBooking.RunAt, _settings.ScheduleWindowMinutes))
        {
            _scheduleTickBusy = true;
            _lastScheduleDate = scheduledBooking.RunAt.Date;
            try
            {
                ApplyScheduledReleaseBooking(scheduledBooking);
                _settings.ScheduledReleaseBooking = null;
                SettingsService.Save(_settings);
                UpdateScheduleStatus();
                AppendLog($"到达 12:29，开始执行 {scheduledBooking.TargetDate} 的一次性提前监测抢场计划。");
                await RunBookingLoopAsync(scheduledBooking.TargetDate, true);
            }
            catch (Exception exception)
            {
                SelectionStatusText.Text = "一次性抢场计划启动失败：" + exception.Message;
                AppendLog(SelectionStatusText.Text);
            }
            finally
            {
                _scheduleTickBusy = false;
            }
            return;
        }

        var release = now.Date + BookingSchedule.ReleaseTime;
        if (!_settings.DailyAutoBooking
            || now < release
            || now >= release.AddMinutes(1)
            || _lastScheduleDate == now.Date)
        {
            return;
        }

        _scheduleTickBusy = true;
        _lastScheduleDate = now.Date;
        try
        {
            AppendLog("到达 12:30，开始执行后一天羽毛球自动抢场任务。");
            await RunBookingLoopAsync(BookingSchedule.GetTargetDate(now));
        }
        finally
        {
            _scheduleTickBusy = false;
        }
    }

    private void DailyScheduleCheckBox_Changed(object sender, RoutedEventArgs e) => SaveSchedulePreferences();

    private void SaveSchedulePreferences()
    {
        if (_initializing)
        {
            return;
        }

        _settings.DailyAutoBooking = DailyScheduleCheckBox.IsChecked == true;
        SettingsService.Save(_settings);
        UpdateScheduleStatus();
    }

    private void UpdateScheduleStatus()
    {
        var now = DateTime.Now;
        MonitorNowButton.Content = $"开始次日监测（{BookingSchedule.GetTargetDate(now)}）";
        UpdateScheduledReleaseStatus(now);
        if (_bookingCancellation is not null && _isReleaseMonitoring)
        {
            ScheduleStatusText.Text = "次日放场监测正在执行，发现可订场地会自动提交。";
            return;
        }
        if (_bookingCancellation is not null && _lastScheduleDate == now.Date)
        {
            ScheduleStatusText.Text = "今日 12:30 放场任务正在执行。";
            return;
        }

        ScheduleStatusText.Text = DailyScheduleCheckBox.IsChecked == true
            ? $"下次放场任务：{BookingSchedule.GetNextRun(now):yyyy-MM-dd HH:mm}（本机时间）"
            : "每日自动任务未启用，可手动开始次日监测。";
    }

    private void UpdateScheduledReleaseStatus(DateTime now)
    {
        var task = _settings.ScheduledReleaseBooking;
        CancelScheduledReleaseButton.IsEnabled = task is not null;
        ScheduleReleaseButton.Content = task is null ? "按当前选择设置 12:29 监测抢场" : "替换当前 12:29 计划";
        if (task is null)
        {
            ScheduledReleaseStatusText.Text = "尚未设置一次性 12:29 监测抢场计划。";
            return;
        }

        if (now >= task.RunAt.AddMinutes(_settings.ScheduleWindowMinutes))
        {
            ScheduledReleaseStatusText.Text = $"计划 {task.RunAt:yyyy-MM-dd HH:mm} 已错过，请重新设置；应用需在计划时间运行。";
            return;
        }

        var times = string.Join("、", new[] { task.PreferredTime, task.SecondaryPreferredTime }
            .Where(time => TimeSlots.Contains(time))
            .Distinct());
        var court = string.IsNullOrWhiteSpace(task.CourtPreference) ? "全部场地" : task.CourtPreference;
        ScheduledReleaseStatusText.Text = $"计划：{task.RunAt:MM-dd HH:mm} 抢 {task.TargetDate} · {task.Campus} · {times} · {court}";
    }

    private void BrowseProxyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择代理程序",
            Filter = "可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            ProxyPathTextBox.Text = dialog.FileName;
        }
    }

    private void StartProxyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettingsFromControls(false);
            _proxyService.Start(_settings.ProxyExecutablePath, _settings.ProxyArguments);
        }
        catch (Exception exception)
        {
            AppendLog("启动代理失败：" + exception.Message);
            MessageBox.Show(this, exception.Message, "启动代理失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateProxyStatus();
        }
    }

    private void StopProxyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _proxyService.Stop();
        }
        catch (Exception exception)
        {
            AppendLog("停止代理失败：" + exception.Message);
        }
        finally
        {
            UpdateProxyStatus();
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettingsFromControls(true);
        }
        catch (Exception exception)
        {
            SelectionStatusText.Text = exception.Message;
        }
    }

    private void SaveSettingsFromControls(bool showConfirmation)
    {
        if (!int.TryParse(RefreshTextBox.Text, out var refreshSeconds) || refreshSeconds is < 3 or > 60)
        {
            throw new InvalidOperationException("重试间隔必须是 3 到 60 秒之间的整数。");
        }
        if (!int.TryParse(ProxyPortTextBox.Text, out var proxyPort) || proxyPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("代理端口必须是 1 到 65535 之间的整数。");
        }
        if (string.IsNullOrWhiteSpace(ProxyHostTextBox.Text))
        {
            throw new InvalidOperationException("代理主机不能为空。");
        }
        if (GetPreferredTimes().Count == 0)
        {
            throw new InvalidOperationException("请至少设置一个候选时段。");
        }

        _settings.SelectedAccountId = SelectedAccount?.Id ?? _settings.SelectedAccountId;
        _settings.AutoLogin = AutoLoginCheckBox.IsChecked == true;
        _settings.AutoOpenBadminton = AutoOpenCheckBox.IsChecked == true;
        _settings.DailyAutoBooking = DailyScheduleCheckBox.IsChecked == true;
        _settings.Campus = GetComboText(CampusComboBox);
        _settings.PreferredTime = TimeComboBox.Text.Trim();
        _settings.SecondaryPreferredTime = SecondaryTimeComboBox.Text.Trim();
        _settings.CourtPreference = GetCourtPreference();
        _settings.RefreshSeconds = refreshSeconds;
        _settings.UseBrowserProxy = UseProxyCheckBox.IsChecked == true;
        _settings.ProxyScheme = GetComboText(ProxySchemeComboBox);
        _settings.ProxyHost = ProxyHostTextBox.Text.Trim();
        _settings.ProxyPort = proxyPort;
        _settings.ProxyExecutablePath = ProxyPathTextBox.Text.Trim();
        _settings.ProxyArguments = ProxyArgumentsTextBox.Text;
        _settings.StopProxyOnExit = StopProxyOnExitCheckBox.IsChecked == true;
        SettingsService.Save(_settings);

        if (showConfirmation)
        {
            AppendLog("全部设置已保存。浏览器代理变更将在下次启动时生效。");
            SelectionStatusText.Text = "全部设置已保存；浏览器代理变更将在下次启动时生效。";
        }
    }

    private void UpdateProxyStatus()
    {
        ProxyStatusText.Text = "代理进程：" + _proxyService.StatusText;
        StartProxyButton.IsEnabled = !_proxyService.IsRunning;
        StopProxyButton.IsEnabled = _proxyService.IsRunning;
    }

    private bool EnsureBrowserReady()
    {
        if (_browserReady)
        {
            return true;
        }

        SelectionStatusText.Text = "浏览器尚未就绪，请稍候或检查 WebView2 运行时。";
        return false;
    }

    private async Task<string> RunScriptAsync(string script)
    {
        if (!_browserReady)
        {
            throw new InvalidOperationException("浏览器尚未初始化。");
        }

        return await WebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async Task<string> RunAsyncScriptAsync(string script)
    {
        if (!_browserReady)
        {
            throw new InvalidOperationException("浏览器尚未初始化。");
        }

        var parameters = JsonSerializer.Serialize(new
        {
            expression = script,
            awaitPromise = true,
            returnByValue = true,
            userGesture = true
        });
        var response = await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        if (root.TryGetProperty("exceptionDetails", out var exceptionDetails))
        {
            var message = exceptionDetails.TryGetProperty("text", out var text)
                ? text.GetString() ?? "页面脚本执行失败"
                : "页面脚本执行失败";
            throw new InvalidOperationException(message);
        }

        if (!root.TryGetProperty("result", out var result)
            || !result.TryGetProperty("value", out var value))
        {
            throw new InvalidOperationException("学校接口未返回可读取的预约结果。");
        }

        return value.GetRawText();
    }

    private static string ToJs(string value) => JsonSerializer.Serialize(value);

    private static bool ReadOk(string json) => ReadBoolean(json, "ok");

    private static bool ReadBoolean(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string ReadMessage(string json, string fallback) => ReadString(json, "message", fallback);

    private static string ReadCourt(string json) => ReadString(json, "court", "可用场地");

    private static string ReadString(string json, string property, string fallback = "")
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() ?? fallback : fallback;
    }

    private static string GetComboText(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? string.Empty : comboBox.Text;

    private static void SelectComboText(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.Text = value;
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if (LogTextBox.Text.Length > 30000)
        {
            LogTextBox.Text = LogTextBox.Text[^30000..];
        }
        LogTextBox.ScrollToEnd();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _scheduleTimer.Stop();
        _bookingCancellation?.Cancel();
        if (_settings.StopProxyOnExit)
        {
            _proxyService.Dispose();
        }
    }
}
