using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tweakly;

public sealed record Navigation(string Id, string Title, string Glyph, int Count, bool Selected);
public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private readonly bool preview;
    private bool busy;
    private int revision;
    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindDescendants<T>(child)) yield return descendant;
        }
    }
    private readonly List<Button> actionButtons = [];
    private readonly List<string> selectedApps = [], selectedDevices = [];
    private static readonly (string Id, string En, string Fa, string Glyph)[] Categories =
    [ ("system","Windows","ویندوز","◇"), ("cpu","Processor","پردازنده","▣"), ("gpu","Graphics","گرافیک","◈"), ("power","Power","برق و انرژی","ϟ"), ("memory","Memory","حافظه","▤"), ("storage","Storage","ذخیره‌سازی","▥"), ("network","Network","شبکه","⌁"), ("input","Mouse & keyboard","موس و کیبورد","⌘"), ("debloat","Debloat & apps","برنامه‌ها و Debloat","⊟"), ("cleanup","Cleanup","پاک‌سازی","⌑"), ("extras","Extras","امکانات جانبی","✦"), ("recovery","Recovery & fixes","بازیابی و تعمیر","↶") ];
    public MainWindow(bool preview = false)
    {
        this.preview = preview; InitializeComponent(); DataContext = vm;
        UiMotion.SetEnabled(this, !preview && SystemParameters.ClientAreaAnimation);
        Width = Math.Min(1360, SystemParameters.WorkArea.Width - 28); Height = Math.Min(900, SystemParameters.WorkArea.Height - 28);
        MinWidth = Math.Min(820, SystemParameters.WorkArea.Width - 20); MinHeight = Math.Min(620, SystemParameters.WorkArea.Height - 20);
        Closing += (_, e) => { if (busy) { e.Cancel = true; MessageBox.Show(vm.L("An operation is running. Wait for its result before closing.", "یک عملیات در حال اجراست؛ پیش از بستن برنامه منتظر نتیجه بمانید."), "Tweakly"); } };
        Loaded += (_, _) => Render();
    }
    private Brush Brush(string name) => (Brush)FindResource(name);
    private TextBlock Text(string value, double size = 14, string brush = "TextBrush", bool bold = false) => new() { Text = value, FontSize = vm.Persian ? size + 1 : size, Foreground = Brush(brush), FontWeight = bold ? FontWeights.Bold : FontWeights.Medium, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, Margin = new(0, 0, 0, 8) };
    private Border Card(UIElement child, bool accent = false) => new() { Style = (Style)FindResource("Card"), Child = child, Margin = new(0, 0, 0, 18), Background = accent ? new LinearGradientBrush(Color.FromRgb(38, 30, 67), Color.FromRgb(23, 23, 39), 20) : Brush("SurfaceBrush") };
    private Button Button(string title, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button { Content = title, Style = (Style)FindResource(primary ? "PrimaryButton" : typeof(Button)), Margin = new(0, 0, 10, 8), IsEnabled = !busy };
        button.Click += handler; return button;
    }
    private string CategoryTitle(string id) { var category = Categories.FirstOrDefault(c => c.Id == id); return vm.L(category.En ?? "All options", category.Fa ?? "همهٔ گزینه‌ها"); }
    private void Render()
    {
        revision++; actionButtons.Clear();
        FontFamily = (FontFamily)FindResource(vm.Persian ? "PersianFont" : "EnglishFont");
        AppLayout.FlowDirection = vm.Persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        BrandSubtitle.Text = vm.L("Portable Windows tuning", "تنظیم ویندوز، بدون نصب");
        SetNavigation(OverviewButton, "⌂", vm.L("Overview", "نمای کلی"), vm.Category == "overview"); CategoriesCaption.Text = vm.L("YOUR TOOLBOX", "جعبه‌ابزار تو");
        CategoryNav.ItemsSource = Categories.Select(c => new Navigation(c.Id, vm.L(c.En, c.Fa), c.Glyph, Catalog.All.Count(t => t.Category == c.Id), vm.Category == c.Id)).ToList();
        SetNavigation(HistoryButton, "◷", vm.L("Change history", "تاریخچهٔ تغییرات"), vm.Category == "history"); SetNavigation(AboutButton, "ⓘ", vm.L("About & settings", "درباره و تنظیمات"), vm.Category == "about");
        PortableNote.Text = vm.L("PORTABLE. READY WHEN YOU ARE.", "پرتابل؛ آمادهٔ اجرا");
        LanguageButton.Content = vm.Persian ? "English" : "فارسی";
        LanguageButton.FontFamily = (FontFamily)FindResource(vm.Persian ? "EnglishFont" : "PersianFont");
        LanguageButton.FlowDirection = vm.Persian ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        PageEyebrow.Text = vm.L("YOUR SYSTEM, IN YOUR HANDS", "کنترل سیستم در دست تو");
        FooterNote.Text = vm.L("Open source · Individual actions · No automatic tweaks", "متن‌باز · اجرای جداگانه · بدون تغییر خودکار");
        BusyLabel.Text = vm.L("Working on your selected option. Please keep Tweakly open…", "اجرای گزینهٔ انتخاب‌شده؛ لطفاً Tweakly را باز نگه دارید…");
        PageContent.Children.Clear();
        if (vm.Selected is not null) RenderDetail(vm.Selected);
        else switch (vm.Category) { case "overview": RenderOverview(); break; case "history": RenderHistory(); break; case "about": RenderAbout(); break; default: RenderOptions(); break; }
    }
    private void SetNavigation(Button button, string glyph, string title, bool selected)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(29) }); row.ColumnDefinitions.Add(new());
        var icon = Text(glyph, 17, "AccentBrush"); icon.FontFamily = (FontFamily)FindResource("IconFont"); icon.Margin = new(0); row.Children.Add(icon);
        var label = Text(title, 14); label.Margin = new(0); Grid.SetColumn(label, 1); row.Children.Add(label);
        button.Content = row; button.Background = selected ? new SolidColorBrush(Color.FromRgb(42, 34, 63)) : Brushes.Transparent;
        button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(75, 59, 111)) : Brushes.Transparent;
    }
    private TextBlock TechnicalText(string value, double size = 13)
    {
        var text = Text(value, size, "MutedBrush"); text.FlowDirection = FlowDirection.LeftToRight;
        text.TextAlignment = vm.Persian ? TextAlignment.Right : TextAlignment.Left;
        text.FontFamily = (FontFamily)FindResource("EnglishFont"); return text;
    }
    private string? Incompatibility(Tweak tweak)
    {
        var reason = vm.System.Incompatible(tweak);
        if (!vm.Persian || reason is null) return reason;
        if (reason.Contains("x64 is required")) return "به ویندوز ۱۰ نسخهٔ 22H2 یا ویندوز ۱۱ با معماری x64 نیاز دارد.";
        if (reason.StartsWith("This option targets")) return "این گزینه برای ویندوز " + tweak.Os + " است.";
        if (reason.Contains("hardware was not detected")) return "سخت‌افزار " + tweak.Vendor + " در سیستم شناسایی نشد.";
        if (reason.Contains("Ethernet")) return "کارت شبکهٔ اترنت در سیستم شناسایی نشد.";
        return reason;
    }
    private void RenderOverview()
    {
        PageTitle.Text = vm.L("Make Windows feel like yours.", "ویندوز را مطابق سلیقه‌ات تنظیم کن.");
        var hero = new StackPanel(); hero.Children.Add(Text(vm.L("A quieter interface. More control.", "رابط خلوت‌تر؛ کنترل بیشتر."), 28, bold: true));
        hero.Children.Add(Text(vm.L("Explore your toolbox, choose an option, and apply only the change you want. Your settings stay untouched until you ask.", "جعبه‌ابزار را مرور کن، یک گزینه را انتخاب کن و فقط همان تغییر را اجرا کن. تا انتخاب تو، تنظیمات سیستم دست‌نخورده می‌مانند."), 15, "MutedBrush"));
        var links = new WrapPanel { Margin = new(0, 14, 0, 0) };
        links.Children.Add(Button(vm.L("Browse all " + Catalog.All.Count + " options →", "مرور " + Catalog.All.Count + " گزینه ←"), (_, _) => Navigate("all"), true));
        links.Children.Add(Button(vm.L("Create restore point", "ساخت نقطهٔ بازیابی"), (_, _) => Select(Catalog.Get("restore-point")))); hero.Children.Add(links);
        PageContent.Children.Add(Card(hero, true));
        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new(0, 0, 0, 9) };
        foreach (var (label, value) in new[] { (vm.L("OPERATING SYSTEM", "سیستم‌عامل"), vm.System.Os + " · " + vm.System.Build), (vm.L("MEMORY", "حافظه"), $"{vm.System.RamGb:0} GB"), (vm.L("GRAPHICS", "گرافیک"), string.Join(" + ", vm.System.Gpus.Select(g => g.Vendor).Distinct())) })
        {
            var content = new StackPanel(); content.Children.Add(Text(label, 11, "MutedBrush")); var metric = Text(value.Length > 0 ? value : "—", 19, bold: true); metric.FontFamily = (FontFamily)FindResource("EnglishFont"); metric.FlowDirection = FlowDirection.LeftToRight; metric.TextAlignment = vm.Persian ? TextAlignment.Right : TextAlignment.Left; content.Children.Add(metric);
            var card = Card(content); card.Margin = new(0, 0, 12, 10); stats.Children.Add(card);
        }
        PageContent.Children.Add(stats);
        PageContent.Children.Add(TechnicalText(vm.System.Cpu));
        PageContent.Children.Add(Text(vm.L("Choose your workspace", "بخش موردنظرت را انتخاب کن"), 21, bold: true));
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        tiles.SizeChanged += (_, _) => { var columns = Math.Max(1, (int)(tiles.ActualWidth / 235)); if (tiles.Columns != columns) tiles.Columns = columns; };
        foreach (var category in Categories)
        {
            var item = new StackPanel(); var icon = Text(category.Glyph, 23, "AccentBrush"); icon.FontFamily = (FontFamily)FindResource("IconFont"); item.Children.Add(icon); item.Children.Add(Text(vm.L(category.En, category.Fa), 17, bold: true)); item.Children.Add(Text(Catalog.All.Count(t => t.Category == category.Id) + vm.L(" individual options", " گزینهٔ مستقل"), 12, "MutedBrush"));
            var tile = new Button { Content = item, MinHeight = 122, Margin = new(0, 0, 12, 12), Padding = new(20), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brush("SurfaceBrush"), IsEnabled = !busy };
            tile.Click += (_, _) => Navigate(category.Id); tiles.Children.Add(tile);
        }
        PageContent.Children.Add(tiles);
        var unfinished = vm.History.Count(s => s.Status is "Preparing" or "Running" or "Undoing" or "Failed" or "UndoFailed");
        if (unfinished > 0) PageContent.Children.Add(Text(vm.L($"{unfinished} operation(s) need review in Change history.", $"{unfinished} عملیات در تاریخچه نیازمند بررسی است."), 14, "AccentBrush"));
    }
    private void RenderOptions()
    {
        PageTitle.Text = CategoryTitle(vm.Category);
        var search = new TextBox { Text = vm.Query, Height = 48, Margin = new(0, 0, 0, 16),
            ToolTip = vm.L("Search in Persian or English", "جست‌وجو به فارسی یا انگلیسی") };
        System.Windows.Automation.AutomationProperties.SetName(search, vm.L("Find an option", "جست‌وجوی گزینه"));
        PageContent.Children.Add(Text(vm.L("Find an option", "جست‌وجوی گزینه"), 13, "MutedBrush")); PageContent.Children.Add(search);
        var summary = Text("", 12, "MutedBrush"); PageContent.Children.Add(summary);
        var list = new StackPanel(); PageContent.Children.Add(list);
        void Populate()
        {
            list.Children.Clear(); var options = vm.Visible.ToList();
            summary.Text = vm.L($"{options.Count} options · Choose one to view its controls", $"{options.Count} گزینه · برای دیدن تنظیمات، یک گزینه را انتخاب کن");
            foreach (var tweak in options)
            {
                var incompatible = Incompatibility(tweak);
                var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(28) });
                var copy = new StackPanel { Margin = new(0, 0, 18, 0) };
                var title = Text(tweak.Title(vm.Persian), 17, bold: true); title.Margin = new(0, 0, 0, 6); copy.Children.Add(title);
                var description = Text(incompatible is null ? tweak.Description(vm.Persian) : vm.L("Unavailable: ", "در دسترس نیست: ") + incompatible, 13, "MutedBrush");
                description.LineHeight = 20; description.MaxHeight = 40; description.TextTrimming = TextTrimming.CharacterEllipsis;
                description.Margin = new(0); copy.Children.Add(description); row.Children.Add(copy);
                var badgeText = Text(incompatible is null ? RiskLabel(tweak.Risk) : vm.L("Unavailable", "ناسازگار"), 12,
                    incompatible is not null ? "MutedBrush" : tweak.Risk is "High" or "Destructive" ? "WarningBrush" : "AccentBrush");
                badgeText.Margin = new(0); badgeText.TextWrapping = TextWrapping.NoWrap;
                var badge = new Border { Child = badgeText, Background = new SolidColorBrush(Color.FromRgb(32, 29, 45)),
                    CornerRadius = new(6), Padding = new(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(badge, 1); row.Children.Add(badge);
                var arrow = Text(vm.Persian ? "‹" : "›", 23, "MutedBrush"); arrow.FontFamily = (FontFamily)FindResource("EnglishFont"); arrow.FlowDirection = FlowDirection.LeftToRight;
                arrow.TextAlignment = TextAlignment.Center; arrow.Margin = new(8, 0, 0, 0); arrow.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
                var button = new Button { Content = row, Padding = new(20, 18, 20, 18), MinHeight = 96, Margin = new(0, 0, 0, 12),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brush("SurfaceBrush"), IsEnabled = !busy };
                button.Click += (_, _) => Select(tweak); list.Children.Add(button);
            }
            if (options.Count == 0) list.Children.Add(Card(Text(vm.L("No matches. Try another name or clear the search.", "گزینه‌ای پیدا نشد؛ نام دیگری بنویس یا جست‌وجو را پاک کن."), 15, "MutedBrush")));
        }
        search.TextChanged += (_, _) => { vm.Query = search.Text.Trim(); Populate(); }; Populate();
        if (vm.Category != "all")
        {
            var undo = Button(vm.L("Undo recorded changes in this category", "بازگردانی تغییرات ثبت‌شدهٔ این دسته"), async (_, _) => await Execute(Catalog.All.First(t => t.Category == vm.Category), "undo-category"));
            undo.HorizontalAlignment = HorizontalAlignment.Left;
            undo.IsEnabled = !preview && !busy && vm.History.Any(session => session.Category == vm.Category && session.Undoable && session.Status != "Restored" && session.Changes.Any(change => change.State is "Applied" or "Writing"));
            if (!undo.IsEnabled) undo.ToolTip = vm.L("No recorded changes to restore in this category", "تغییر ثبت‌شده‌ای برای بازگردانی این دسته وجود ندارد");
            ToolTipService.SetShowOnDisabled(undo, true);
            PageContent.Children.Add(undo);
        }
    }
    private string RiskLabel(string risk) => risk switch { "Standard" => vm.L("Standard", "عادی"), "Advanced" => vm.L("Advanced", "پیشرفته"), "High" => vm.L("Sensitive", "حساس"), "Destructive" => vm.L("Removal / reset", "حذف / بازسازی"), _ => risk };
    private string StatusLabel(string status) => vm.Persian ? status switch { "Preparing" => "آماده‌سازی", "Running" => "در حال اجرا", "Completed" => "انجام شد", "CompletedWithSkips" => "انجام شد؛ بعضی موارد ناسازگار بودند", "Failed" => "ناموفق", "Undoing" => "در حال بازگردانی", "Restored" => "بازگردانی شد", "UndoFailed" => "بازگردانی متوقف شد", "Cancelled" => "لغو شد", "Unavailable" => "در دسترس نیست", "Busy" => "عملیات دیگری در حال اجراست", "Opened" => "باز شد", _ => status } : status;
    private void RenderDetail(Tweak tweak)
    {
        PageTitle.Text = CategoryTitle(tweak.Category);
        var back = Button(vm.L("← Back to options", "→ بازگشت به گزینه‌ها"), (_, _) => { if (busy) return; vm.Selected = null; Render(); ContentScroll.ScrollToTop(); UiMotion.Reveal(PageContent); });
        back.HorizontalAlignment = HorizontalAlignment.Left; PageContent.Children.Add(back);
        var detail = new StackPanel(); detail.Children.Add(Text(tweak.Title(vm.Persian), 25, bold: true));
        detail.Children.Add(Text(RiskLabel(tweak.Risk) + (tweak.Reboot ? vm.L("  ·  Restart may be needed", "  ·  ممکن است ری‌استارت لازم باشد") : vm.L("  ·  No automatic restart", "  ·  بدون ری‌استارت خودکار")), 12, "AccentBrush"));
        detail.Children.Add(Text(tweak.Description(vm.Persian), 15, "MutedBrush"));
        if (tweak.Risk is "High" or "Destructive") detail.Children.Add(Text(tweak.Destructive ? vm.L("This action may not have an exact Undo. A verified restore point is required before it starts.", "این عملیات ممکن است بازگردانی دقیق نداشته باشد. پیش از اجرا باید نقطهٔ بازیابی با موفقیت ساخته شود.") : vm.L("Sensitive system change. Review its effects and technical changes before applying.", "تغییر حساس سیستم؛ اثرات و جزئیات آن را پیش از اجرا بررسی کن."), 14, "AccentBrush"));
        var inputs = new StackPanel { Margin = new(0, 15, 0, 10) }; detail.Children.Add(inputs);
        var state = Text(vm.L("Reading current state…", "در حال خواندن وضعیت فعلی…"), 13, "MutedBrush"); detail.Children.Add(state);
        var controls = new WrapPanel { Margin = new(0, 14, 0, 0) };
        var apply = Button(FeatureBuilder.ReadOnly(tweak) ? vm.L("Open / run", "بازکردن / اجرا") : vm.L("Apply this option", "اجرای همین گزینه"), async (_, _) => await Execute(tweak, "apply"), true);
        apply.IsEnabled = !preview && !busy && Incompatibility(tweak) is null; actionButtons.Add(apply); controls.Children.Add(apply);
        if (!FeatureBuilder.Maintenance(tweak) && !FeatureBuilder.ReadOnly(tweak))
        {
            var undo = Button(vm.L("Undo my changes", "بازگردانی تغییرات من"), async (_, _) => await Execute(tweak, "undo")); undo.IsEnabled = !busy && new Journal(vm.System.Sid).Latest(tweak.Id) is not null; actionButtons.Add(undo); controls.Children.Add(undo);
        }
        detail.Children.Add(controls);
        if (tweak.Handler is "RemoveApps" or "RemoveCopilot" or "RemoveEdge" or "InstallEdge" or "RegisterApps" or "Ping" or "Bufferbloat") detail.Children.Add(Text(vm.L("Network / package availability may be required for this option or its recovery.", "این گزینه یا بازیابی آن ممکن است به اینترنت یا فایل نصب برنامه نیاز داشته باشد."), 12, "MutedBrush"));
        PageContent.Children.Add(Card(detail));
        var technical = new StackPanel();
        var expander = new Expander { Header = vm.L("Review technical changes", "بررسی تغییرات فنی"), Content = technical }; PageContent.Children.Add(expander);
        if (FeatureBuilder.Maintenance(tweak)) technical.Children.Add(Text(vm.L("Only the selected maintenance action is executed. Its result is recorded in history; changes inside external Windows tools are controlled by those tools.", "فقط عملیات انتخاب‌شده اجرا و نتیجه ثبت می‌شود؛ تغییرات داخل ابزارهای جداگانهٔ ویندوز با همان ابزار انجام می‌شوند."), 13, "MutedBrush"));
        var version = revision;
        _ = FillInputsAndProbe(tweak, inputs, state, technical, apply, version);
    }
    private void AddCombo(StackPanel container, string key, string label, IEnumerable<Choice> choices, string? selected = null, bool chooseFirst = true)
    {
        container.Children.Add(Text(label, 13, "MutedBrush"));
        var items = choices.ToList(); var combo = new ComboBox { ItemsSource = items, MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new(0, 0, 0, 16) };
        combo.SelectedItem = items.FirstOrDefault(c => c.Value == (vm.Inputs.GetValueOrDefault(key) ?? selected)) ?? (chooseFirst ? items.FirstOrDefault() : null);
        if (combo.SelectedItem is Choice initial) vm.Inputs[key] = initial.Value;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c) vm.Inputs[key] = c.Value; };
        container.Children.Add(combo);
    }
    private void AddField(StackPanel container, string key, string label, string defaultValue)
    {
        container.Children.Add(Text(label, 13, "MutedBrush")); var input = new TextBox { Text = vm.Inputs.GetValueOrDefault(key, defaultValue), Height = 48, Margin = new(0, 0, 0, 16), FlowDirection = key == "policy" && vm.Persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, FontFamily = (FontFamily)FindResource(key == "policy" && vm.Persian ? "PersianFont" : "EnglishFont") };
        vm.Inputs[key] = input.Text; input.TextChanged += (_, _) => vm.Inputs[key] = input.Text.Trim(); container.Children.Add(input);
    }
    private async Task FillInputsAndProbe(Tweak tweak, StackPanel inputs, TextBlock state, StackPanel technical, Button apply, int version)
    {
        try
        {
            switch (tweak.Input)
            {
                case "ram": AddCombo(inputs, "ram", vm.L("Installed memory", "حجم رم نصب‌شده"), new[] { 4, 8, 12, 16, 24, 32, 48, 64, 128 }.Select(n => new Choice(n.ToString(), n + " GB")), new[] { 4, 8, 12, 16, 24, 32, 48, 64, 128 }.MinBy(n => Math.Abs(n - vm.System.RamGb)).ToString()); break;
                case "queue": AddCombo(inputs, "queue", vm.L("Data queue size", "اندازهٔ صف داده"), [new("19", vm.L("19 · high-end CPU", "۱۹ · پردازندهٔ قوی")), new("24", vm.L("24 · mid-range CPU", "۲۴ · پردازندهٔ متوسط")), new("36", vm.L("36 · entry-level CPU", "۳۶ · پردازندهٔ ضعیف‌تر"))]); break;
                case "drive": AddCombo(inputs, "drive", vm.L("Target drive", "درایو هدف"), DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select(d => new Choice(d.Name[..1], d.Name + " · " + d.VolumeLabel))); break;
                case "adapter": case "mtu":
                    AddCombo(inputs, "adapter", vm.L("Target network adapter", "کارت شبکهٔ هدف"), vm.System.Adapters.Where(a => !tweak.EthernetOnly || a.Ethernet).Select(a => new Choice(a.Id, a.ToString())));
                    if (tweak.Input == "mtu") AddField(inputs, "mtu", vm.L("MTU · 1280–1500", "MTU · از ۱۲۸۰ تا ۱۵۰۰"), "1500"); break;
                case "region": AddCombo(inputs, "region", vm.L("Server region", "منطقهٔ سرور"), new[] { ("Europe", "اروپا"), ("NA East", "شرق آمریکای شمالی"), ("NA Central", "مرکز آمریکای شمالی"), ("NA West", "غرب آمریکای شمالی"), ("Brazil", "برزیل"), ("Asia", "آسیا"), ("Middle East", "خاورمیانه"), ("Oceania", "اقیانوسیه") }.Select((label, i) => new Choice(FeatureBuilder.Regions[i], vm.L(label.Item1, label.Item2))), "me"); break;
                case "qos":
                    AddField(inputs, "policy", vm.L("Policy name", "نام سیاست"), "Tweakly Game"); AddField(inputs, "application", vm.L("Application EXE name", "نام EXE برنامه"), "");
                    inputs.Children.Add(Button(vm.L("Choose an EXE…", "انتخاب فایل EXE…"), (_, _) => { var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true }; if (picker.ShowDialog(this) == true) { vm.Inputs["application"] = Path.GetFileName(picker.FileName); Render(); } })); break;
                case "startup": AddCombo(inputs, "startup", vm.L("Choose one startup entry to disable", "یک برنامهٔ شروع را برای غیرفعال‌کردن انتخاب کن"), FeatureBuilder.StartupChoices(vm.System).Select(s => new Choice(s.Id, s.Label)), chooseFirst: false); break;
                case "apps":
                    inputs.Children.Add(Text(vm.L("Select the apps to remove. Nothing is selected by default.", "برنامه‌های موردنظر را انتخاب کن؛ به‌صورت پیش‌فرض هیچ‌کدام انتخاب نشده‌اند."), 13, "MutedBrush"));
                    var appChecks = new StackPanel();
                    foreach (var (id, label) in FeatureBuilder.InboxApps) { var check = new CheckBox { Content = TechnicalText(label, 13), IsChecked = selectedApps.Contains(id) }; check.Checked += (_, _) => { if (!selectedApps.Contains(id)) selectedApps.Add(id); vm.Inputs["apps"] = string.Join(',', selectedApps); }; check.Unchecked += (_, _) => { selectedApps.Remove(id); vm.Inputs["apps"] = string.Join(',', selectedApps); }; appChecks.Children.Add(check); }
                    inputs.Children.Add(new ScrollViewer { Content = appChecks, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); break;
                case "disk": case "devices":
                    inputs.Children.Add(Text(vm.L("Reading detected devices…", "در حال شناسایی دستگاه‌ها…"), 13, "MutedBrush"));
                    var devices = preview ? new List<Choice> { new("preview", "Sample device (preview only)") } : await FeatureBuilder.DeviceChoices(tweak.Input == "disk" ? "DiskDrive" : "", tweak.Input == "disk");
                    if (version != revision) return;
                    inputs.Children.Clear();
                    if (tweak.Input == "disk") AddCombo(inputs, "disk", vm.L("Target disk (power-loss protection required)", "دیسک هدف (حفاظت قطع برق ضروری است)"), devices, chooseFirst: false);
                    else
                    {
                        inputs.Children.Add(Text(vm.L("Select disconnected devices to remove", "دستگاه‌های قطع‌شدهٔ موردنظر را انتخاب کن"), 13, "MutedBrush"));
                        foreach (var device in devices) { var check = new CheckBox { Content = device.Label, IsChecked = selectedDevices.Contains(device.Value) }; check.Checked += (_, _) => { if (!selectedDevices.Contains(device.Value)) selectedDevices.Add(device.Value); vm.Inputs["devices"] = string.Join('\n', selectedDevices); }; check.Unchecked += (_, _) => { selectedDevices.Remove(device.Value); vm.Inputs["devices"] = string.Join('\n', selectedDevices); }; inputs.Children.Add(check); }
                    }
                    break;
            }
            if (Incompatibility(tweak) is string unsupported) { state.Text = vm.L("Unavailable: ", "در دسترس نیست: ") + unsupported; apply.IsEnabled = false; return; }
            if (tweak.Handler is "InstallEdge" or "RemoveEdge" && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe")))
            {
                state.Text = vm.L("Unavailable: Microsoft App Installer / winget is required for this online action.", "در دسترس نیست: این عملیات آنلاین به Microsoft App Installer یا winget نیاز دارد."); apply.IsEnabled = false; return;
            }
            if (FeatureBuilder.Maintenance(tweak)) { state.Text = vm.L("Ready for the selected action. Result is verified where Windows exposes it.", "آمادهٔ عملیات انتخاب‌شده؛ نتیجه بر اساس اطلاعات قابل بررسی ویندوز گزارش می‌شود."); return; }
            if (preview) { state.Text = vm.L("Preview · actions disabled", "پیش‌نمایش · اجرای عملیات غیرفعال است"); apply.IsEnabled = false; foreach (var op in tweak.Operations.Take(12)) technical.Children.Add(TechnicalText(op.ToString(), 11)); return; }
            // Parameterized operations are probed once the user asks to inspect them; no setters run here.
            var valuesPanel = new StackPanel();
            technical.Children.Add(Button(vm.L("Read current values and planned changes", "خواندن مقادیر فعلی و تغییرات این گزینه"), async (_, _) =>
            {
                valuesPanel.Children.Clear(); state.Text = vm.L("Reading…", "در حال خواندن…");
                try
                {
                    var operations = await Task.Run(() => FeatureBuilder.Build(tweak, new(vm.Inputs), vm.System));
                    var backend = new WindowsBackend(); var supported = 0; var matching = 0; var readingsNeedAdmin = false;
                    foreach (var op in operations)
                    {
                        var reading = await Task.Run(() => backend.Read(op)); if (version != revision) return;
                        readingsNeedAdmin |= reading.RequiresElevation;
                        if (reading.Supported) { supported++; if (WindowsBackend.Desired(op, reading)) matching++; }
                        valuesPanel.Children.Add(TechnicalText(op + "\n" + (reading.Supported ? "Current: " + Readable(op, reading) : "Unavailable: " + reading.Reason), 11));
                    }
                    state.Text = vm.L($"{supported} readable settings · {matching} already match · {operations.Count - supported} unavailable", $"{supported} تنظیم قابل خواندن · {matching} مطابق هدف · {operations.Count - supported} ناموجود");
                    apply.IsEnabled = !preview && !busy && (supported > 0 || readingsNeedAdmin);
                }
                catch (Exception error) { state.Text = error.Message; }
            }));
            technical.Children.Add(valuesPanel);
            state.Text = vm.L("Current state: not yet read. Expand technical changes to inspect it.", "وضعیت فعلی هنوز خوانده نشده؛ برای بررسی، تغییرات فنی را باز کن.");
            if (tweak.Input.Length == 0 && tweak.Operations.Count is > 0 and < 15)
            {
                var ops = await Task.Run(() => FeatureBuilder.Build(tweak, new(vm.Inputs), vm.System)); var backend = new WindowsBackend(); var reads = new List<(Operation Op, Reading Read)>();
                foreach (var op in ops) reads.Add((op, await Task.Run(() => backend.Read(op))));
                if (version != revision) return;
                var supported = reads.Count(r => r.Read.Supported); var match = reads.Count(r => r.Read.Supported && WindowsBackend.Desired(r.Op, r.Read));
                state.Text = vm.L($"Current state: {match}/{supported} settings match this option", $"وضعیت فعلی: {match} از {supported} تنظیم مطابق این گزینه است");
                if (supported == 0)
                {
                    var adminNeeded = reads.Any(r => r.Read.RequiresElevation);
                    state.Text = adminNeeded ? vm.L("Administrator is needed to read these settings; they will be checked before applying.", "برای خواندن این تنظیمات Administrator لازم است؛ پیش از اجرا بررسی می‌شوند.") : vm.L("Unavailable: no compatible settings found.", "در دسترس نیست: تنظیم سازگاری پیدا نشد.");
                    apply.IsEnabled = !preview && adminNeeded && !busy;
                }
            }
        }
        catch (Exception error) { if (version == revision) state.Text = vm.L("Could not read: ", "خواندن ممکن نشد: ") + error.Message; }
    }
    private static string Readable(Operation op, Reading reading)
    {
        if (!reading.Exists) return "[not set]";
        if (WindowsBackend.Normalize(op).Kind == "Registry") { var snap = JsonSerializer.Deserialize<RegSnapshot>(reading.Value, Catalog.Json)!; return snap.Value is null ? "[registry subtree]" : string.Join("; ", snap.Value.Data); }
        return reading.Value.Length > 500 ? "[backup of current state]" : reading.Value;
    }
    private async Task Execute(Tweak tweak, string mode)
    {
        if (preview || busy) return;
        if (mode == "apply")
        {
            string[] required = tweak.Input switch { "ram" => ["ram"], "queue" => ["queue"], "drive" => ["drive"], "adapter" => ["adapter"], "mtu" => ["adapter", "mtu"], "qos" => ["policy", "application"], "startup" => ["startup"], "apps" => ["apps"], "disk" => ["disk"], "devices" => ["devices"], "region" => ["region"], _ => [] };
            if (required.Any(key => !vm.Inputs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
            {
                MessageBox.Show(this, vm.L("Complete the fields and select a target first. Nothing was changed.", "ابتدا ورودی‌ها را کامل و مورد هدف را انتخاب کن؛ هیچ تغییری انجام نشده است."), "Tweakly", MessageBoxButton.OK, MessageBoxImage.Information); return;
            }
        }
        if (mode != "apply" || tweak.Risk is "High" or "Destructive")
        {
            var confirmation = mode == "apply" ? tweak.Description(vm.Persian) + "\n\n" + (tweak.Destructive ? vm.L("A verified restore point will be created first. Exact Undo may not be available.", "ابتدا نقطهٔ بازیابی ساخته می‌شود؛ ممکن است بازگردانی دقیق موجود نباشد.") : vm.L("Only this option will run.", "فقط همین گزینه اجرا می‌شود.")) : vm.L("Restore only changes recorded by Tweakly? Newer external changes will be preserved.", "فقط تغییرات ثبت‌شدهٔ Tweakly بازگردانی شوند؟ تغییرات جدیدِ خارج از برنامه حفظ می‌شوند.");
            if (MessageBox.Show(this, confirmation, tweak.Title(vm.Persian), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        }
        var request = new Request(tweak.Id, mode, new(vm.Inputs));
        busy = true; BusyBanner.Visibility = Visibility.Visible; CategoryNav.IsEnabled = false; OverviewButton.IsEnabled = false; HistoryButton.IsEnabled = false; AboutButton.IsEnabled = false; LanguageButton.IsEnabled = false;
        foreach (var button in actionButtons) button.IsEnabled = false;
        Result result;
        try { result = mode == "apply" && FeatureBuilder.ReadOnly(tweak) ? await Maintenance.RunInteractive(tweak, request.Inputs) : await Worker.Invoke(request, CancellationToken.None); }
        catch (Exception error) { result = new(false, "Failed", error.Message); }
        finally { busy = false; BusyBanner.Visibility = Visibility.Collapsed; CategoryNav.IsEnabled = OverviewButton.IsEnabled = HistoryButton.IsEnabled = AboutButton.IsEnabled = LanguageButton.IsEnabled = true; }
        try { ActivityLog.Save(tweak, result, vm.System.Sid); }
        catch (Exception error) { result = result with { Message = result.Message + "\n" + vm.L("Could not record the UI result: ", "ثبت نتیجه در تاریخچه ممکن نشد: ") + error.Message }; }
        Render();
        var panel = new StackPanel(); panel.Children.Add(Text(vm.L("Result · ", "نتیجه · ") + StatusLabel(result.Status), 19, result.Success ? "GreenBrush" : "AccentBrush", true));
        var message = result.Status == "Cancelled" ? vm.L("Administrator permission was cancelled. Nothing was changed.", "مجوز Administrator لغو شد؛ هیچ تنظیمی تغییر نکرد.") : result.Status == "Restored" && result.Success ? vm.L("Your recorded previous configuration was restored and verified.", "تنظیمات قبلیِ ثبت‌شده بازیابی و نتیجه بررسی شد.") : result.Message;
        if (result.Success && result.Status is "Completed" or "CompletedWithSkips" && result.Session is { Undoable: true } recorded)
        {
            message = vm.L($"{recorded.Changes.Count(c => c.State == "Applied")} changed · {recorded.Changes.Count(c => c.State == "Unchanged")} already set · {recorded.Messages.Count} unavailable", $"{recorded.Changes.Count(c => c.State == "Applied")} تغییر · {recorded.Changes.Count(c => c.State == "Unchanged")} از قبل مطابق هدف · {recorded.Messages.Count} مورد ناموجود") + "\n" + string.Join('\n', recorded.Messages);
        }
        panel.Children.Add(Text(message, 13, "MutedBrush"));
        if (result.Reboot) panel.Children.Add(Text(vm.L("Restart Windows when convenient. Tweakly will not restart it automatically.", "در زمان مناسب ویندوز را ری‌استارت کن؛ Tweakly خودکار ری‌استارت نمی‌کند."), 13, "AccentBrush"));
        PageContent.Children.Insert(Math.Min(1, PageContent.Children.Count), Card(panel)); ContentScroll.ScrollToTop();
    }
    private void RenderHistory()
    {
        PageTitle.Text = vm.L("Your changes, recorded.", "تغییرات تو، ثبت‌شده.");
        PageContent.Children.Add(Text(vm.L("Exact Undo uses your original settings. Maintenance actions may require System Restore or reinstalling packages.", "بازگردانی دقیق از تنظیمات اصلی تو استفاده می‌کند. عملیات نگهداری ممکن است به بازیابی سیستم یا نصب مجدد برنامه نیاز داشته باشند."), 14, "MutedBrush"));
        if (vm.History.Any(s => s.Undoable && s.Changes.Any(c => c.State is "Writing" or "Applied")))
            PageContent.Children.Add(Button(vm.L("Undo all recorded setting changes", "بازگردانی همهٔ تغییرات ثبت‌شدهٔ تنظیمات"), async (_, _) => await Execute(Catalog.Get("general"), "undo-all")));
        var sessions = vm.History;
        if (sessions.Count == 0) PageContent.Children.Add(Card(Text(vm.L("No changes yet. Your system settings have not been changed by Tweakly.", "هنوز تغییری ثبت نشده؛ Tweakly تنظیمات سیستم را تغییر نداده است."), 17)));
        foreach (var session in sessions)
        {
            var tweak = Catalog.All.FirstOrDefault(t => t.Id == session.TweakId); var panel = new StackPanel();
            panel.Children.Add(Text(tweak?.Title(vm.Persian) ?? session.TweakId, 17, bold: true)); panel.Children.Add(Text(session.Started.ToLocalTime().ToString("yyyy/MM/dd HH:mm") + " · " + StatusLabel(session.Status), 12, "AccentBrush"));
            panel.Children.Add(Text($"{session.Changes.Count(c => c.State == "Applied")} " + vm.L("recorded changes", "تغییر ثبت‌شده"), 12, "MutedBrush"));
            if (session.Error.Length > 0) panel.Children.Add(Text(session.Error, 13, "MutedBrush"));
            if (tweak is not null) panel.Children.Add(Button(vm.L("Open option / recovery", "بازکردن گزینه / بازیابی"), (_, _) => Select(tweak)));
            if (session.Messages.Count > 0)
            {
                var details = string.Join('\n', session.Messages.Select(m => m.StartsWith("Power plan backup") ? m[..Math.Min(m.IndexOf(':'), 90)] + " · saved in protected journal" : m));
                panel.Children.Add(new Expander { Header = vm.L("Details", "جزئیات"), Content = TechnicalText(details, 11) });
            }
            PageContent.Children.Add(Card(panel));
        }
    }
    private void RenderAbout()
    {
        PageTitle.Text = vm.L("Small app. Clear intentions.", "اپ ساده؛ عملکرد روشن.");
        var panel = new StackPanel(); var name = Text("Tweakly 0.1.2", 27, bold: true); name.FlowDirection = FlowDirection.LeftToRight; name.FontFamily = (FontFamily)FindResource("EnglishFont"); name.TextAlignment = vm.Persian ? TextAlignment.Right : TextAlignment.Left; panel.Children.Add(name); panel.Children.Add(Text(vm.L("Open-source Windows tuning, one option at a time. Built with C# / WPF / .NET. MIT license.", "ابزار متن‌باز تنظیم ویندوز با اجرای جداگانهٔ گزینه‌ها. ساخته‌شده با C#، WPF و .NET. مجوز MIT."), 15, "MutedBrush"));
        panel.Children.Add(Text(vm.L("No account, telemetry or background service. The core toolbox works offline. Windows prompts for Administrator only when an operation needs it.", "بدون حساب کاربری، تله‌متری یا سرویس پس‌زمینه. امکانات اصلی آفلاین کار می‌کنند؛ ویندوز فقط هنگام عملیات نیازمند دسترسی، Administrator درخواست می‌کند."), 14, "MutedBrush"));
        panel.Children.Add(Text(vm.L("Backups and history", "بکاپ و تاریخچه"), 18, bold: true)); panel.Children.Add(TechnicalText(Journal.Root, 12));
        panel.Children.Add(Button(vm.L("Open history folder", "بازکردن پوشهٔ تاریخچه"), (_, _) => { if (Directory.Exists(Journal.Root)) Process.Start(new ProcessStartInfo(Journal.Root) { UseShellExecute = true }); }));
        panel.Children.Add(Text(vm.L("Compatibility: Windows 10 22H2 / Windows 11 x64. A setting's availability depends on your edition, installed components and driver. Experimental driver / registry switches do not guarantee improved performance.", "سازگاری: ویندوز ۱۰ نسخهٔ 22H2 و ویندوز ۱۱، x64. موجودبودن هر تنظیم به نسخه، اجزای نصب‌شده و درایور بستگی دارد؛ تنظیمات آزمایشی تضمین افزایش کارایی ندارند."), 13, "MutedBrush"));
        panel.Children.Add(Text(vm.L("Microsoft Windows and NVIDIA drivers are system prerequisites; they are not redistributed. Artwork and power plan are original Tweakly resources.", "ویندوز Microsoft و درایور NVIDIA پیش‌نیاز سیستم‌اند و در اپ بازتوزیع نشده‌اند. طرح برق و گرافیک متعلق به Tweakly هستند."), 12, "MutedBrush"));
        panel.Children.Add(Button(vm.L("License & third-party notices", "مجوز و اطلاعات وابستگی‌ها"), (_, _) =>
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var text = string.Join("\n\n────────────────────\n\n", assembly.GetManifestResourceNames().Where(n => n.Contains("LICENSE") || n.Contains("NOTICES")).Select(n => { using var stream = assembly.GetManifestResourceStream(n)!; using var reader = new StreamReader(stream); return n + "\n\n" + reader.ReadToEnd(); }));
            var dialog = new Window { Owner = this, Title = "Tweakly · Licenses", FontFamily = (FontFamily)FindResource("EnglishFont"), Width = Math.Min(850, SystemParameters.WorkArea.Width - 30), Height = Math.Min(650, SystemParameters.WorkArea.Height - 30), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("BackgroundBrush"), Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(16), FlowDirection = FlowDirection.LeftToRight } };
            dialog.ShowDialog();
        }));
        PageContent.Children.Add(Card(panel));
        PageContent.Children.Add(Button(vm.L("Switch to Persian", "تغییر زبان به انگلیسی"), (_, _) => Language_Click(this, new())));
    }
    private void Navigate(string category) { if (busy) return; vm.Category = category; vm.Selected = null; vm.Query = ""; Render(); ContentScroll.ScrollToTop(); UiMotion.Reveal(PageContent); }
    private void Select(Tweak tweak) { if (busy) return; vm.Category = tweak.Category; vm.Selected = tweak; vm.Inputs.Clear(); selectedApps.Clear(); selectedDevices.Clear(); Render(); ContentScroll.ScrollToTop(); UiMotion.Reveal(PageContent); }
    private void Overview_Click(object sender, RoutedEventArgs e) => Navigate("overview");
    private void Category_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string id }) Navigate(id); }
    private void History_Click(object sender, RoutedEventArgs e) => Navigate("history");
    private void About_Click(object sender, RoutedEventArgs e) => Navigate("about");
    private void Language_Click(object sender, RoutedEventArgs e) { if (busy) return; vm.Persian = !vm.Persian; if (!preview) vm.SavePreferences(); Render(); UiMotion.Reveal(PageContent); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    public async Task CaptureQa(string directory)
    {
        Directory.CreateDirectory(directory); Width = 1360; Height = 900;
        async Task Capture(string name, double dpi = 96)
        {
            await Dispatcher.InvokeAsync(() => { UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
            var image = new RenderTargetBitmap((int)(ActualWidth * dpi / 96), (int)(ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32); image.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
        }
        vm.Persian = false; Navigate("overview"); await Capture("overview-en");
        Navigate("cpu"); await Capture("cpu-en");
        Navigate("network"); await Capture("network-en");
        Select(Catalog.Get("qos")); await Capture("qos-en");
        vm.Persian = true; vm.Selected = null; vm.Category = "overview"; Render(); await Capture("overview-fa");
        Navigate("cpu"); await Capture("cpu-fa");
        Select(Catalog.Get("qos")); await Capture("qos-fa");
        var field = FindDescendants<TextBox>(PageContent).First(); field.Focus(); await Capture("focus-fa");
        Navigate("cpu"); var search = FindDescendants<TextBox>(PageContent).First(); search.Text = "Intel"; await Capture("search-fa"); search.Text = "missing-option"; await Capture("empty-search-fa");
        Select(Catalog.All.First(t => t.Category == "cpu" && t.Vendor == "Intel"));
        FindDescendants<Expander>(PageContent).First().IsExpanded = true; ContentScroll.ScrollToBottom(); await Capture("technical-fa");
        Select(Catalog.Get("remove-apps")); await Capture("apps-fa");
        Width = 850; Height = 680; Navigate("overview"); await Capture("small-fa");
        Navigate("history"); await Capture("history-fa");
        MinHeight = 450; Height = 510; Width = 920; Select(Catalog.Get("mtu")); await Capture("compact-form-fa", 192);
        // Read-only checks exercise page animation and the inherited reduced-motion switch.
        var fonts = new[] { "EnglishFont", "PersianFont" }.ToDictionary(key => key, key =>
            ((FontFamily)FindResource(key)).GetTypefaces().Select(face => face.TryGetGlyphTypeface(out var glyph) ? glyph.FontUri.ToString() : "unresolved").ToArray());
        UiMotion.SetEnabled(this, true); Navigate("cpu");
        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Render);
        await Task.Delay(80); var duringAnimation = PageContent.Opacity;
        await Task.Delay(220); var afterAnimation = PageContent.Opacity;
        var motionButton = FindDescendants<Button>(PageContent).First(); motionButton.ApplyTemplate();
        var motionTriggers = motionButton.Template.Triggers.OfType<MultiTrigger>().ToArray();
        var hoverStory = ((System.Windows.Media.Animation.BeginStoryboard)motionTriggers[0].EnterActions[0]).Storyboard.Clone();
        var pressStory = ((System.Windows.Media.Animation.BeginStoryboard)motionTriggers[1].EnterActions[0]).Storyboard.Clone();
        hoverStory.Begin(motionButton, motionButton.Template, true); pressStory.Begin(motionButton, motionButton.Template, true);
        await Task.Delay(220);
        var hoverOpacity = ((Border)motionButton.Template.FindName("Hover", motionButton)).Opacity;
        var pressedScale = ((ScaleTransform)((Grid)motionButton.Template.FindName("Root", motionButton)).RenderTransform).ScaleX;
        hoverStory.Remove(motionButton); pressStory.Remove(motionButton);
        UiMotion.SetEnabled(this, false); Navigate("overview");
        var disabledMotion = PageContent.Opacity;
        var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(directory, "ui-checks.json"), JsonSerializer.Serialize(new { Fonts = fonts, DuringPageAnimation = duringAnimation, AfterPageAnimation = afterAnimation, ReducedMotionOpacity = disabledMotion, HoverOpacity = hoverOpacity, PressedScale = pressedScale }, Catalog.Json));
        File.WriteAllText(Path.Combine(directory, "runtime.json"), JsonSerializer.Serialize(new { WorkingSetMiB = process.WorkingSet64 / 1048576d, PrivateMiB = process.PrivateMemorySize64 / 1048576d, OptionCount = Catalog.All.Count, Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), ReadOnlyQa = true }, Catalog.Json));
    }
}
