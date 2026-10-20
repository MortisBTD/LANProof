using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LANProof.App.Controls;
using LANProof.Runtime;
using LANProof.Runtime.Companion;
using LANProof.Core.Diagnostics;
using LANProof.Core.Models;
using LANProof.Export;

namespace LANProof.App;

public sealed partial class MainWindow : Window
{
    private static readonly IBrush Ink = GlassDesign.Ink, Muted = GlassDesign.Muted,
        Green = GlassDesign.Green, Red = GlassDesign.Red, Blue = GlassDesign.Blue;
    private readonly MonitoringSession _session;
    private CompanionHost? _companion;
    private string _pairingCode="";
    private string _timelineDevice=Environment.MachineName;
    private List<ProbeResult> LocalMeasurements()=>_measurements.Where(p=>p.DeviceId==Environment.MachineName).ToList();
    private readonly string _appearancePath;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly List<Action> _liveBindings = [];
    private readonly List<Action> _persistentBindings = [];
    private readonly Border _noticeBar = new();
    private HistoryWindow? _history;
    private int _historyPeriod = -1, _updateQueued;
    private List<ProbeResult> _selectedCycle = [];
    internal int VisiblePeriodMeasurementCount => _report.TotalMeasurements;
    internal long HistoryRowsRead => _history?.TotalRowsRead ?? 0;
    private readonly ContentControl _body = new() { Name="PageContent",Margin = new Thickness(3, 3, 10, 10) };
    private ScrollViewer? _scroll;
    private Button? _saveSettings;
    private DateTime? _selectedTime;
    private readonly TextBlock _heading = Text("Обзор сети", 34, bold: true);
    private readonly TextBlock _status = Text("Подготовка…", 12, Muted);
    private readonly TextBlock _notice = Text("", 12, Muted);
    private readonly TextBlock _device = Text(Environment.MachineName, 12, bold: true);
    private readonly TextBlock _intro = Text("Ваше соединение в деталях", 13, Muted);
    private readonly TextBlock _live = Text("Подготовка", 11, Green, true);
    private readonly ComboBox _period = new()
    {
        ItemsSource = new[] { "15 минут", "Последний час", "24 часа", "7 дней" }, SelectedIndex = 1, Width = 160,
        CornerRadius = new CornerRadius(6), Background = Brush("#AFFFFFFF"), BorderBrush = Brush("#DFFFFFFF"),
        FontSize = 12, Padding = new Thickness(14, 9)
    };
    private readonly Button _toggle;
    private readonly Button _once;
    private readonly Button _themeToggle;
    private readonly List<Button> _navigation = [];
    private readonly List<NetworkGlyph> _navGlyphs = [];
    private readonly List<TextBlock> _navLabels = [];
    private List<ProbeResult> _measurements = [];
    private List<NetworkEvent> _events = [];
    private DiagnosticReport _report = new();
    private DateTime _periodStart;
    private DateTime _periodEnd;
    private bool _ready, _busy, _canClose;
    private int _page;
    private string _message = "";
    private readonly string[] _titles = ["Обзор сети", "История событий", "Диагностика", "Отчёты", "Настройки", "Android-компаньон"];

    public MainWindow(MonitoringSession? session = null, bool autoStart = true, string? appearancePath = null)
    {
        _session = session ?? new MonitoringSession();
        _appearancePath = appearancePath ?? ApplicationPaths.ThemeFile;
        Title = "LANProof — диагностика соединения";
        Width = 1380; Height = 920; MinWidth = 1100; MinHeight = 760;
        Background = Brushes.Transparent; Foreground = Ink; FontFamily = GlassDesign.UiFont;
        TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
        TransparencyBackgroundFallback = Brush("#E8EEF6");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://LANProof/Assets/AppIcon.ico")));
        _themeToggle = Button(GlassDesign.IsDark ? "Светлая тема" : "Тёмная тема", async () =>
        {
            var dark = !GlassDesign.IsDark;
            await new ThemePreferences { DarkMode = dark }.SaveAsync(_appearancePath);
            GlassDesign.SetDark(dark);
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            _themeToggle!.Content = dark ? "Светлая тема" : "Тёмная тема";
            foreach (var visual in this.GetVisualDescendants())
            {
                if (visual is Border border && border.Classes.Contains("glass-surface")) border.BoxShadow = GlassDesign.Shadows;
                visual.InvalidateVisual();
            }
        });
        _themeToggle.Name = "ThemeToggle";
        _toggle = Button("Начать мониторинг", async () =>
        {
            if (_session.IsRunning) { await _session.StopAsync(); _message = "Мониторинг остановлен. История сохранена."; }
            else { _session.Start(); _message = "Идёт сбор реальных измерений…"; }
            _saveSettings?.SetCurrentValue(IsEnabledProperty, !_session.IsRunning);
            await RefreshAsync();
        }, accent: true);
        _once = Button("Проверить сейчас", async () =>
        {
            _message = "Выполняются сетевые проверки…"; UpdateHeader();
            await _session.MeasureAsync(_lifetime.Token);
            _message = "Разовая проверка завершена.";
            await RefreshAsync();
        });
        _toggle.Name = "MonitoringToggle"; _once.Name = "MeasureOnce";
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("218,*"), Margin = new Thickness(18) };
        var side = new DockPanel();
        var logoIcon = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10),
            Background = GlassDesign.Gradient("#70B6FF", "#087CF4"), BorderBrush = Brush("#90FFFFFF"), BorderThickness = new Thickness(1),
            Child = new LogoMark { Width = 34, Height = 34 } };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(2, 7, 0, 0) };
        brand.Children.Add(logoIcon); brand.Children.Add(Stack(2, Text("LANProof", 21, bold: true), Text("Всё о вашем соединении", 9, Muted)));
        var logo = Stack(0, brand);
        DockPanel.SetDock(logo, Dock.Top); side.Children.Add(logo);
        var foot = Stack(14,
            new Border { Padding = new Thickness(0,10),
                Child = Stack(8, Text("Это устройство", 12, Muted), _device, Text("Данные остаются на ПК", 12, Muted)) },
            Text("LANProof 0.2 beta  ·  Windows", 10, Muted));
        DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
        var nav = new StackPanel { Spacing = 8, Margin = new Thickness(0, 28, 0, 28) };
        var labels = new[] { "Обзор", "История", "Диагностика", "Отчёты", "Настройки", "Компаньон" };
        for (int i = 0; i < labels.Length; i++)
        {
            var page = i;
            var button = Button(labels[i], () => { _page = page; RenderPage(); return Task.CompletedTask; });
            var glyph = new NetworkGlyph { Kind = i, Stroke = Muted, Width = 20, Height = 20 };
            var label = Text(labels[i], 13, Muted, true);
            var navContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { glyph, label } };
            button.Content = navContent;
            button.CornerRadius = new CornerRadius(6); button.MinHeight = 44;
            button.Padding = new Thickness(14, 12);
            _navGlyphs.Add(glyph); _navLabels.Add(label);
            button.Name = "Nav" + i; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _navigation.Add(button); nav.Children.Add(button);
        }
        DockPanel.SetDock(nav,Dock.Top); side.Children.Add(nav);
        side.Children.Add(new ScrollViewer {Content=SidebarChain(),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});
        shell.Children.Add(GlassDesign.ShellSurface(side));

        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(28, 8, 6, 0) };
        Grid.SetColumn(main, 1); shell.Children.Add(main);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(Stack(7,Text("LANProof / Домашняя сеть",11,Muted),_heading,_intro));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_themeToggle); actions.Children.Add(_once); actions.Children.Add(_toggle); Grid.SetColumn(actions, 1); header.Children.Add(actions);
        main.Children.Add(header);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 18) };
        toolbar.Children.Add(_status);
        var rightTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        rightTools.Children.Add(new Border { CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 8), Child = _live });
        rightTools.Children.Add(_period);
        Grid.SetColumn(rightTools, 1); toolbar.Children.Add(rightTools); Grid.SetRow(toolbar, 2); main.Children.Add(toolbar);
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        _scroll = scroll;
        Grid.SetRow(scroll, 3); main.Children.Add(scroll);
        _notice.FontSize=13; _noticeBar.Child=_notice; _noticeBar.Padding=new Thickness(12,10);
        _noticeBar.CornerRadius=new CornerRadius(6); _noticeBar.Margin=new Thickness(0,0,0,12);
        _noticeBar.BorderBrush=GlassDesign.Divider; _noticeBar.BorderThickness=new Thickness(1); _noticeBar.IsVisible=false;
        Grid.SetRow(_noticeBar,1); main.Children.Add(_noticeBar);
        var privacy=Text("Измерения и история хранятся на этом устройстве",11,Muted); privacy.Margin=new Thickness(2,12,0,0);
        Grid.SetRow(privacy,4); main.Children.Add(privacy);
        var scene = new Grid(); scene.Children.Add(new GlassBackdrop()); scene.Children.Add(shell); Content = scene;

        _period.SelectionChanged += async (_, _) => { if (_ready) await Guard(RefreshAsync); };
        _session.Updated += OnSessionUpdated;
        Opened += async (_, _) => await Guard(async () =>
        {
            await Task.Run(_session.InitializeAsync);
            _ready = true;
            await RefreshAsync();
            if (autoStart) _session.Start();
            UpdateHeader();
        });
        Closing += async (_, args) =>
        {
            if (_canClose) return;
            args.Cancel = true;
            _ready = false;
            _session.Updated -= OnSessionUpdated;
            _lifetime.Cancel();
            try { await _session.StopAsync(); }
            catch (Exception ex) { _message = ex.Message; }
            if(_companion!=null) await _companion.DisposeAsync();
            _canClose = true; Close();
        };
        RenderPage();
    }

    private void OnSessionUpdated()
    {
        if (Interlocked.Exchange(ref _updateQueued,1)==1) return;
        Dispatcher.UIThread.Post(async () =>
        {
            try { if (_ready) await Guard(RefreshAsync); }
            finally { Volatile.Write(ref _updateQueued,0); }
        });
    }

    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _message = "Ошибка: " + ex.Message; UpdateHeader(); }
    }

    private async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            int period=_period.SelectedIndex;
            var end=DateTime.UtcNow;
            var start=end- (period switch { 0=>TimeSpan.FromMinutes(15),2=>TimeSpan.FromDays(1),3=>TimeSpan.FromDays(7),_=>TimeSpan.FromHours(1) });
            var history=_history;
            if (history==null || _historyPeriod!=period)
                history=await Task.Run(()=>HistoryWindow.LoadAsync(_session,start,end,_lifetime.Token));
            else await Task.Run(()=>history.RefreshAsync(start,end,_lifetime.Token));
            if (_lifetime.IsCancellationRequested || period!=_period.SelectedIndex) return;
            _history=history; _historyPeriod=period; _periodStart=start; _periodEnd=end;
            _measurements=history.Preview; _events=history.Events; _report=history.Report;
            foreach (var update in _liveBindings.ToArray()) update();
            UpdateHeader();
        }
        finally { _refreshGate.Release(); }
    }

    private void UpdateHeader()
    {
        _toggle.Content = _session.IsRunning ? "Приостановить" : "Начать мониторинг";
        _toggle.IsEnabled = _ready && !_busy;
        _once.IsEnabled = _ready && !_busy && !_session.IsRunning;
        if (_saveSettings != null) _saveSettings.IsEnabled = !_busy && !_session.IsRunning;
        var last = LocalMeasurements().LastOrDefault()?.Timestamp;
        _status.FontFamily=GlassDesign.NumericFont;
        _status.Text = last.HasValue ? $"Последний цикл: {last.Value.ToLocalTime():HH:mm:ss}" : "Измерений пока нет";
        if (last.HasValue && last.Value.ToLocalTime().Date != DateTime.Today) _status.Text = $"Последнее измерение {last.Value.ToLocalTime():dd.MM в HH:mm:ss}";
        _status.Foreground = Muted;
        _live.Text = _session.IsRunning ? "●  В реальном времени" : "○  На паузе";
        _live.Foreground = _session.IsRunning ? Green : Muted;
        _notice.Text = _session.Error ?? _message;
        bool error=_session.Error!=null || _message.StartsWith("Ошибка:",StringComparison.Ordinal);
        _notice.Foreground=error ? Red : Ink;
        _noticeBar.Background=error ? Brush("#90FBE4E7") : Brush("#70EDF1F6");
        _noticeBar.IsVisible=!string.IsNullOrEmpty(_notice.Text);
        foreach (var update in _persistentBindings) update();
    }

    private void RenderPage()
    {
        _liveBindings.Clear();
        _heading.Text = _titles[_page];
        for (int i = 0; i < _navigation.Count; i++)
        {
            _navigation[i].Background = i == _page ? GlassDesign.Gradient("#F9FFFFFF", "#ACFFFFFF") : Brushes.Transparent;
            _navigation[i].BorderBrush = i == _page ? Brush("#FFFFFFFF") : Brushes.Transparent;
            _navigation[i].BorderThickness = new Thickness(1);
            _navLabels[i].Foreground = i == _page ? Blue : Muted;
            _navGlyphs[i].Stroke = i == _page ? Blue : Muted;
            _navGlyphs[i].InvalidateVisual();
        }
        _period.IsVisible = _page is not (4 or 5);
        _intro.Text = _page switch { 1=>"Что происходило с соединением",2=>"Причины отклонений и подтверждающие данные",3=>"Выгрузка измерений за период",4=>"Параметры проверок и хранение",_=>"Сводка по последнему циклу" };
        var offset = _scroll?.Offset ?? default;
        _body.Content = _page switch { 1 => Timeline(), 2 => Diagnostics(), 3 => Reports(), 4 => Settings(), 5=>Companion(), _ => Overview() };
        if (_scroll != null) Dispatcher.UIThread.Post(() => _scroll.Offset = offset, DispatcherPriority.Loaded);
        UpdateHeader();
    }

    private bool? CycleHealthy()
    {
        var local=LocalMeasurements();var time=local.LastOrDefault()?.Timestamp;
        var cycle=local.Where(p=>p.Timestamp==time).ToList();
        if (cycle.Count==0 || cycle.Any(p=>!p.IsConclusive)) return null;
        return cycle.All(p=>p.IsSuccess && !(p.PacketLossPercent>0)
            && !(p.ProbeType is ProbeType.Gateway or ProbeType.External && (p.RoundTripMs>=_session.Configuration.LatencyWarningMs || p.JitterMs>=_session.Configuration.JitterWarningMs))
            && !(p.SignalStrengthDbm<=_session.Configuration.WiFiSignalWarningDbm));
    }
    private string HeroTitle()=>!_session.IsRunning ? "Наблюдение на паузе" : CycleHealthy() switch
    { true=>"Соединение стабильно",false=>"Зафиксированы отклонения",_=>"Ожидаем результаты проверок" };
    private string HeroDescription()=>!_session.IsRunning ? "История сохранена. Начните мониторинг или выполните разовую проверку."
        : CycleHealthy() switch { true=>"Последний цикл завершён без отклонений.",false=>"Изучите детали измерений в истории и диагностике.",_=>"Отсутствие результата не доказывает исправность или отказ соединения." };

    private Control Overview()
    {
        var state=LiveText(()=>"●  "+HeroTitle(),22,bold:true);
        _liveBindings.Add(()=>state.Foreground=!_session.IsRunning ? Muted : CycleHealthy() switch { true=>Green,false=>Red,_=>Muted });
        var summary=Card(Stack(10,state,LiveText(HeroDescription,13,Muted),
            LiveText(()=>$"Последний цикл ПК: {(LocalMeasurements().LastOrDefault()?.Timestamp.ToLocalTime().ToString("HH:mm:ss") ?? "нет данных")}  ·  Период запуска: {_session.Configuration.IntervalSeconds} с",11,Muted)));
        var bar=new Grid { ColumnDefinitions=new ColumnDefinitions("*,*,*,*") };
        var metrics=new[]
        {
            Metric("Ping интернета",ProbeType.External,p=>p.RoundTripMs,"мс",p=>p.RoundTripMs>=_session.Configuration.LatencyWarningMs),
            Metric("Джиттер",ProbeType.External,p=>p.JitterMs,"мс",p=>p.JitterMs>=_session.Configuration.JitterWarningMs),
            Metric("Потеря ICMP",ProbeType.External,p=>p.PacketLossPercent,"%",p=>p.PacketLossPercent>0),
            Metric("До шлюза",ProbeType.Gateway,p=>p.RoundTripMs,"мс",p=>p.RoundTripMs>=_session.Configuration.LatencyWarningMs)
        };
        for (int i=0;i<metrics.Length;i++)
        {
            var cell=new Border { Padding=new Thickness(18,4), BorderBrush=GlassDesign.Divider,
                BorderThickness=i<3 ? new Thickness(0,0,1,0) : new Thickness(0), Child=metrics[i] };
            Grid.SetColumn(cell,i); bar.Children.Add(cell);
        }
        var chart=new LatencyChart { Measurements=LocalMeasurements(), SelectedTime=_selectedTime,MinHeight=130,Height=190 };
        _liveBindings.Add(()=> { chart.Measurements=LocalMeasurements(); chart.InvalidateVisual(); });
        var detail=LiveText(()=>_selectedTime is {} time ? CycleSummary(time) : "Выберите цикл на графике для просмотра измерений.",13,Muted);
        chart.TimeSelected+=time=> { _selectedTime=time; detail.Text=CycleSummary(time); };
        return Stack(16,summary,Card(bar),Card(Stack(14,Row(Text("Задержка по времени",16,bold:true),Legend()),chart,detail,
            LiveText(()=>$"Проверок за период: {_report.TotalMeasurements:N0}  ·  Успешность: {Pct(_report.SuccessRatePercent)}  ·  Средняя потеря ICMP по партиям: шлюз {Pct(_report.GatewayPacketLossPercent)}, интернет {Pct(_report.ExternalPacketLossPercent)}",13,Muted))));
    }

    private Control Timeline()
    {
        var selected=new StackPanel { Spacing=9 };
        var devices=_measurements.Select(p=>p.DeviceId).Append(Environment.MachineName).Distinct().Order().ToList();
        var selector=new ComboBox {ItemsSource=devices,SelectedItem=_timelineDevice,MinWidth=260,FontSize=13};
        var chart=new LatencyChart { Measurements=_measurements.Where(p=>p.DeviceId==_timelineDevice).ToList(),SelectedTime=_selectedTime,Height=280 };
        selector.SelectionChanged+=(_,_)=> {if(selector.SelectedItem is string device){_timelineDevice=device;_selectedTime=null;chart.SelectedTime=null;chart.Measurements=_measurements.Where(p=>p.DeviceId==device).ToList();chart.InvalidateVisual();selected.Children.Clear();}};
        _liveBindings.Add(()=> {
            var current=_measurements.Select(p=>p.DeviceId).Append(Environment.MachineName).Distinct().Order().ToList();
            if(!devices.SequenceEqual(current)){devices=current;selector.ItemsSource=current;selector.SelectedItem=_timelineDevice;}
            chart.Measurements=_measurements.Where(p=>p.DeviceId==_timelineDevice).ToList(); chart.InvalidateVisual(); });
        async Task SelectCycle(DateTime time)
        {
            _selectedTime=time;
            var cycle=await _session.ReadCycleAsync(time,_lifetime.Token);
            if (_selectedTime!=time) return;
            _selectedCycle=cycle; selected.Children.Clear();
            selected.Children.Add(Text($"Цикл {time.ToLocalTime():dd.MM.yyyy HH:mm:ss}",16,bold:true));
            foreach (var p in cycle)
                selected.Children.Add(Text($"{EventDetector.Label(p.ProbeType)} · {p.DeviceId} · {ProbeState(p)} · {Ms(p.RoundTripMs)}\n{p.Details}",13,ProbeColor(p)));
        }
        chart.TimeSelected+=time=>_ = Guard(()=>SelectCycle(time));
        if (_selectedTime is {} time) _ = Guard(()=>SelectCycle(time));
        else selected.Children.Add(Text("Выберите точку на графике. Подробности выбранного цикла появятся здесь.",13,Muted));
        var eventList=new StackPanel { Spacing=12 };
        string? fingerprint=null;
        void UpdateEvents()
        {
            var next=string.Join(";",_events.Take(150).Select(e=>$"{e.Id}:{e.EndTime:O}:{e.Title}"))+":"+_report.TotalEvents;
            if (fingerprint==next) return; fingerprint=next;
            eventList.Children.Clear(); eventList.Children.Add(Text($"События за период · {_report.TotalEvents:N0}",16,bold:true));
            if (_report.TotalEvents==0) eventList.Children.Add(Text("За выбранный период сетевых событий не зафиксировано.",13,Muted));
            foreach (var e in _events.Take(150)) eventList.Children.Add(Card(Stack(8,
                Row(Text(e.Title,16,e.Severity==EventSeverity.Critical ? Red : Ink,true),Text(e.StartTime.ToLocalTime().ToString("dd.MM HH:mm:ss"),13,Muted)),
                Text($"{e.DeviceId} · {e.Layer} · "+(e.EndTime is {} end ? $"Конец наблюдения: {end.ToLocalTime():HH:mm:ss}" : "Активно по последнему измерению"),13,Muted),Text(e.Description,13,Muted))));
            if (_report.TotalEvents>150) eventList.Children.Add(Text("Показаны последние 150 событий. В JSON/CSV экспортируется весь период.",13,Muted));
        }
        UpdateEvents(); _liveBindings.Add(UpdateEvents);
        return Stack(16,Card(Stack(12,Row(Text("Временная шкала",16,bold:true),Legend()),selector,chart,
            Text("Красные отметки — неудачные ICMP-проверки; серые — незавершённые. Отсутствие измерений не означает нормальную работу.",13,Muted))),
            new Border { Padding=new Thickness(2,10),Child=selected },eventList);
    }

    private Control Diagnostics()
    {
        var advice=new StackPanel { Spacing=12 };
        string? fingerprint=null;
        void UpdateAdvice()
        {
            var next=string.Join("|",_report.Recommendations); if (next==fingerprint) return; fingerprint=next;
            advice.Children.Clear(); advice.Children.Add(Text("Следующие действия",16,bold:true));
            for (int i=0;i<_report.Recommendations.Count;i++) advice.Children.Add(Text($"{i+1:00}   {_report.Recommendations[i]}",13));
            if (_report.Recommendations.Count==0) advice.Children.Add(Text("Рекомендации появятся после получения измерений.",13,Muted));
        }
        UpdateAdvice(); _liveBindings.Add(UpdateAdvice);
        return Stack(16,Card(Stack(12,Text("Сопоставление ПК и Android",22,bold:true),LiveText(()=>_report.CrossDeviceFindings.Count==0
            ? "Подтверждённых совпадений пока нет. Нужны измерения обоих устройств с согласованным временем; пропуски и неточные часы не считаются доказательством."
            : string.Join("\n\n",_report.CrossDeviceFindings.Select(f=>$"{f.Summary} · циклов: {f.MatchingCycles}\n{f.Evidence}")),13,Muted))),
            Card(Stack(14,Text("АНАЛИЗ ВЫБРАННОГО ПЕРИОДА",11,Muted,true,letterSpacing:1),
            LiveText(()=>_report.Summary,22,bold:true),
            LiveText(()=>_report.ProbableLayer==null ? (_report.TotalMeasurements>0 && !_report.HasAnomalies ? "Отказов не обнаружено" : "Данных для локализации причины недостаточно")
                : $"Область: {_report.ProbableLayer} · Достоверность гипотезы: {_report.ConfidencePercent}%",13,Muted),
            LiveText(()=>_report.DetailedAnalysis,13,Muted))),Card(advice),Card(Stack(10,Text("Границы диагностики",16,bold:true),
            Text("Измерения выполняются на ПК и сопряжённом Android. ICMP может фильтроваться, DNS — использовать кеш. HTTPS — не сетевой RTT. Общий вывод требует близких циклов и погрешности времени не более ±1 секунды на устройство. Высокая задержка сама по себе не определяет виновника.",13,Muted))));
    }

    private Control Reports()
    {
        var formats=new Grid { ColumnDefinitions=new ColumnDefinitions("*,*,*"),ColumnSpacing=14 };
        var choices=new[] { ("pdf","Отчёт для поддержки","Выводы, рекомендации, до 100 событий и 200 последних измерений."),
            ("json","Полные исходные данные","Все измерения и события периода вместе с диагностическим заключением."),
            ("csv","Данные для таблиц","Все записи в UTF-8; время UTC. ICMP, DNS и HTTPS — в отдельных колонках.") };
        for (int i=0;i<choices.Length;i++)
        {
            var (format,title,description)=choices[i];
            var badge=new Border { CornerRadius=new CornerRadius(999),Padding=new Thickness(9,4),BorderBrush=GlassDesign.Divider,BorderThickness=new Thickness(1),Child=Text(format.ToUpperInvariant(),11,Muted) };
            var card=Card(Stack(16,Row(Text(title,16,bold:true),badge),Text(description,13,Muted),
                Button("Сохранить ."+format,()=>ExportAsync(format.ToUpperInvariant()))));
            Grid.SetColumn(card,i); formats.Children.Add(card);
        }
        return Stack(18,Card(Stack(12,Text("Измерения за выбранный период",22,bold:true),
            LiveText(()=>$"{_periodStart.ToLocalTime():dd.MM HH:mm} — {_periodEnd.ToLocalTime():dd.MM HH:mm}\nИзмерений: {_report.TotalMeasurements:N0} · Событий: {_report.TotalEvents:N0}",13,Muted))),formats,
            Card(Stack(10,Text("Что входит в отчёт",16,bold:true),Text("Имя устройства, адреса, сведения об адаптере и результаты активных проверок. Проверьте эти данные перед передачей отчёта другому человеку. Экспорт сохраняет выбранный файл локально.",13,Muted))));
    }

    private async Task ExportAsync(string format)
    {
        if (_report.TotalMeasurements == 0) { _message = "За выбранный период нет измерений для экспорта."; UpdateHeader(); return; }
        var extension = format.ToLowerInvariant();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить отчёт LANProof", SuggestedFileName = $"LANProof-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}",
            DefaultExtension = extension, FileTypeChoices = [new FilePickerFileType(format) { Patterns = ["*." + extension] }], ShowOverwritePrompt = true
        });
        if (file == null) return;
        var path = file.TryGetLocalPath() ?? throw new IOException("Выберите локальный файл.");
        var snapshot = _report;
        long throughId=_history?.LastId ?? long.MaxValue;
        _message = "Создаётся отчёт…"; UpdateHeader();
        var exporter = new ReportExportService();
        if (format == "PDF") await exporter.ExportToPdfAsync(snapshot, path, _lifetime.Token);
        else if (format == "CSV") await Task.Run(()=>exporter.ExportToCsvAsync(snapshot,
            _session.StreamMeasurementsAsync(snapshot.PeriodStart,snapshot.PeriodEnd,throughId:throughId,ct:_lifetime.Token),
            _session.StreamEventsAsync(snapshot.PeriodStart,snapshot.PeriodEnd,_lifetime.Token),path,_lifetime.Token));
        else await Task.Run(()=>exporter.ExportToJsonAsync(snapshot,
            _session.StreamMeasurementsAsync(snapshot.PeriodStart,snapshot.PeriodEnd,throughId:throughId,ct:_lifetime.Token),
            _session.StreamEventsAsync(snapshot.PeriodStart,snapshot.PeriodEnd,_lifetime.Token),path,_lifetime.Token));
        _message = "Отчёт сохранён: " + path; UpdateHeader();
    }

    private Control Settings()
    {
        var config = _session.Configuration;
        TextBox Input(string value) => new()
        {
            Text = value, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(6),
            Background = Brush("#F0F4F9"), BorderBrush = Brush("#AAB8CA"), BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 11), FontSize = 13
        };
        var interval = Input(config.IntervalSeconds.ToString()); var timeout = Input(config.TimeoutMs.ToString());
        var pings = Input(config.PingsPerCycle.ToString());
        var icmpTimeout=Input(config.IcmpTimeoutMs.ToString()); var cycleTimeout=Input(config.CycleTimeoutMs.ToString());
        var ips = Input(string.Join(", ", config.ExternalTargets)); var domains = Input(string.Join(", ", config.DnsTestDomains));
        var urls = Input(string.Join(", ", config.HttpTargets));
        string[] Split(TextBox input) => (input.Text ?? "").Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Control Field(string label, Control input, string hint) => Stack(7, Text(label, 13, bold: true), input, Text(hint, 11, Muted));
        var save = Button("Сохранить настройки", async () =>
        {
            if (!int.TryParse(interval.Text, out var seconds) || !int.TryParse(timeout.Text, out var ms) || !int.TryParse(pings.Text, out var count)
                || !int.TryParse(icmpTimeout.Text,out var icmpMs) || !int.TryParse(cycleTimeout.Text,out var cycleMs))
                throw new ArgumentException("Интервал, таймаут и количество ping должны быть целыми числами.");
            var next = new ProbeConfiguration { IntervalSeconds = seconds, TimeoutMs = ms, PingsPerCycle = count,
                IcmpTimeoutMs=icmpMs,CycleTimeoutMs=cycleMs,
                ExternalTargets = Split(ips).ToList(), DnsTestDomains = Split(domains).ToList(), HttpTargets = Split(urls).ToList() };
            await _session.SaveConfigurationAsync(next);
            _history=null;
            _message = "Настройки сохранены. При запуске будут использованы новые цели."; UpdateHeader();
        }, accent: true);
        save.IsEnabled = !_session.IsRunning;
        _saveSettings = save;
        return Stack(16, Card(Stack(18, Text("Параметры измерений", 21, bold: true),
            Text("Остановите мониторинг перед изменением настроек. Проверки создают небольшой активный трафик к указанным адресам.", 13, Muted),
            Field("Период запуска циклов, секунды", interval, "Запуск по расписанию без добавления длительности предыдущего цикла. Циклы не пересекаются."),
            Field("Таймаут DNS/HTTPS, мс", timeout, "От 100 до 30000. Разные цели проверяются параллельно."),
            Field("Таймаут одного ICMP-запроса, мс",icmpTimeout,"Стандартно 800 мс."),
            Field("Предельная длительность цикла, мс",cycleTimeout,"Должна вмещать партию ping и DNS/HTTPS и не превышать период запуска."),
            Field("Ping на один адрес за цикл", pings, "От 1 до 10. При одном ответе джиттер не определяется."),
            Field("Внешние IP", ips, "Через запятую. От 1 до 10 IPv4 / IPv6 адресов."),
            Field("Домены DNS", domains, "Проверяется системное разрешение имён; результат может приходить из кеша."),
            Field("Сервисы HTTPS", urls, "HEAD-запросы, ответы 2xx успешны. Время до заголовков включает сервис и холодное соединение, а не только сеть."), save)),
            Card(Stack(10, Text("Хранение и приватность", 17, bold: true),
                Text("SQLite: " + _session.DatabasePath + "\nНастройки: " + _session.SettingsPath, 12, Muted),
                Text("Данные остаются на ПК. Измерения старше 30 дней очищаются при запуске. Приложение не перехватывает пользовательский трафик. При закрытии окна мониторинг останавливается. VPN и виртуальные адаптеры могут влиять на выбор шлюза.", 13, Muted))));
    }

    private Control SidebarChain()
    {
        var chain=Stack(14,Text("УРОВНИ СОЕДИНЕНИЯ",11,Muted,true,letterSpacing:1));
        var types=new[] { ProbeType.Adapter,ProbeType.Gateway,ProbeType.External,ProbeType.Dns,ProbeType.Http };
        var names=new[] { "Устройство","Шлюз","Интернет","DNS","Сервисы HTTPS" };
        for (int i=0;i<types.Length;i++)
        {
            var type=types[i];
            var icon=new NetworkGlyph { Kind=i==0 ? 0 : i==1 ? 5 : i==2 ? 6 : i==3 ? 2 : 3, Width=20,Height=20,Stroke=Muted };
            var dot=Text("●",11,Muted); var state=Text("Нет данных",11,Muted);
            var row=new Grid { ColumnDefinitions=new ColumnDefinitions("20,*,Auto"),ColumnSpacing=9 };
            row.Children.Add(icon); var caption=Stack(3,Text(names[i],13),state); Grid.SetColumn(caption,1);row.Children.Add(caption);
            Grid.SetColumn(dot,2);row.Children.Add(dot);chain.Children.Add(row);
            _persistentBindings.Add(()=> { var probe=Latest(type);state.Text=ProbeState(probe);dot.Foreground=ProbeColor(probe);ToolTip.SetTip(row,probe?.Details ?? "Ожидание измерений"); });
        }
        return chain;
    }
    private TextBlock LiveText(Func<string> get,double size=13,IBrush? color=null,bool bold=false)
    {
        var text=Text(get(),size,color,bold);
        _liveBindings.Add(()=> { var value=get(); if (text.Text!=value) text.Text=value; });
        return text;
    }
    private static string ProbeState(ProbeResult? p)=>p==null ? "Нет данных" : !p.IsConclusive ? "Нет результата" : !p.IsSuccess ? "Нет ответа" : p.PacketLossPercent>0 ? "Частичный сбой" : "Доступно";
    private static IBrush ProbeColor(ProbeResult? p)=>p==null || !p.IsConclusive ? Muted : !p.IsSuccess || p.PacketLossPercent>0 ? Red : Green;
    private static string Pct(double? value)=>value.HasValue ? $"{value.Value:F1}%" : "нет данных";
    private ProbeResult? Latest(ProbeType type) => _measurements.LastOrDefault(p => p.ProbeType == type && p.DeviceId==Environment.MachineName);
    private string CycleSummary(DateTime time) => time.ToLocalTime().ToString("dd.MM HH:mm:ss") + "  ·  " + string.Join("  /  ",
        _measurements.Where(x => x.Timestamp == time && x.ProbeType is ProbeType.Gateway or ProbeType.External).Select(x => EventDetector.Label(x.ProbeType) + ": " + (x.IsSuccess ? Ms(x.RoundTripMs) : "нет ответа")));
    private static IBrush lossBrush(ProbeResult? p) => p?.PacketLossPercent > 0 ? Red : Green;
    private static string Ms(double? value) => value.HasValue ? $"{value.Value:F1} мс" : "—";
    private static Control Legend() => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 14,
        Children = { Text("● Роутер", 11, Brush("#9481D9")), Text("● Интернет", 11, Blue) }
    };
    private Control Metric(string title,ProbeType type,Func<ProbeResult,double?> value,string unit,Func<ProbeResult,bool> warning)
    {
        var number=Text("Нет данных",13,Muted);number.FontFamily=GlassDesign.NumericFont;number.HorizontalAlignment=HorizontalAlignment.Right;
        void Update()
        {
            var probe=Latest(type); var measured=probe is {IsConclusive:true} ? value(probe) : null;
            number.Text=measured.HasValue ? $"{measured.Value:F1} {unit}" : probe is {IsConclusive:false} ? "Нет результата" : probe is {IsSuccess:false} ? "Нет ответа" : "Нет данных";
            number.FontSize=measured.HasValue ? 28 : 13;
            number.Foreground=measured.HasValue ? warning(probe!) ? Red : Ink : Muted;
        }
        Update();_liveBindings.Add(Update);
        return Stack(16,Text(title,13,Muted),number);
    }
    private static IBrush Brush(string color) => GlassDesign.Brush(color);
    private static TextBlock Text(string value, double size = 13, IBrush? color = null, bool bold = false,double letterSpacing=0) => new()
    {
        Text = value, FontSize = size<=11 ? 11 : size<=14 ? 13 : size<=19 ? 16 : size<=29 ? 22 : 34,
        Foreground = color ?? Ink, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LetterSpacing = letterSpacing
    };
    private static StackPanel Stack(double spacing, params Control[] controls)
    {
        var stack = new StackPanel { Spacing = spacing };
        foreach (var control in controls) stack.Children.Add(control);
        return stack;
    }
    private static Control Row(Control left, Control right)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        row.Children.Add(left); Grid.SetColumn(right, 1); row.Children.Add(right); return row;
    }
    private static Border Card(Control child) => GlassDesign.Surface(child);
    private Button Button(string label, Func<Task> action, bool accent = false)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(16, 11), CornerRadius = new CornerRadius(6),
            Background = accent ? GlassDesign.Gradient("#4EAAFF", "#0874EA") : Brush("#F7FFFFFF"),
            Foreground = accent ? Brushes.White : Ink, BorderBrush = accent ? Brush("#75FFFFFF") : Brush("#DFFFFFFF"),
            BorderThickness = new Thickness(1), FontWeight = FontWeight.Medium, FontSize = 12,
            Transitions = [new BrushTransition { Property = BackgroundProperty, Duration = TimeSpan.FromMilliseconds(180) }]
        };
        button.Classes.Add(accent ? "glass-primary" : "glass-action");
        button.Click += async (_, _) =>
        {
            if (_busy) return;
            _busy = true; button.IsEnabled = false; UpdateHeader();
            await Guard(action);
            _busy = false; button.IsEnabled = true; UpdateHeader();
        };
        return button;
    }
}
