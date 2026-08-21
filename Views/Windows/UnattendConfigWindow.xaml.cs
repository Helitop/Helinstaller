using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Xml;
using System.Windows;
using Schneegans.Unattend;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Windows
{
    public partial class UnattendConfigWindow : FluentWindow
    {
        // === ВКЛАДКА 1: БЕЗОПАСНОСТЬ И СЕТЬ ===
        public bool BypassRequirementsCheck { get; set; } = true;
        public bool BypassNetworkCheck { get; set; } = true;
        public bool DisableSmartScreen { get; set; } = false;
        public bool DisableSac { get; set; } = false;
        public bool DisableUac { get; set; } = true;
        public bool DisableCoreIsolation { get; set; } = false;
        public bool DisableWpbt { get; set; } = false;

        // === ВКЛАДКА 2: ОПТИМИЗАЦИЯ И СЛУЖБЫ ===
        public bool DisableWindowsUpdate { get; set; } = false;
        public bool DisableWidgets { get; set; } = false;
        public bool DisableSystemRestore { get; set; } = false;
        public bool DisableFastStartup { get; set; } = false;
        public bool PreventDeviceEncryption { get; set; } = true;
        public bool DisablePointerPrecision { get; set; } = false;
        public bool EnableRemoteDesktop { get; set; } = false;
        public bool EnableLongPaths { get; set; } = true;
        public bool DeleteWindowsOld { get; set; } = true;
        public bool PasswordExpirationUnlimited { get; set; } = true;

        // === ВКЛАДКА 3: ИНТЕРФЕЙС И КАСТОМИЗАЦИЯ ===
        public bool ClassicContextMenu { get; set; } = false;
        public bool AlignTaskbarLeft { get; set; } = true;
        public bool HideTaskViewButton { get; set; } = true;
        public bool ShowFileExtensions { get; set; } = true;
        public bool ShowHiddenFiles { get; set; } = true;
        public bool LaunchToThisPC { get; set; } = true;
        public bool HideInfoTip { get; set; } = false;
        public bool ShowAllTrayIcons { get; set; } = false;
        public bool SystemColorThemeDark { get; set; } = true;
        public bool AppsColorThemeDark { get; set; } = true;
        public bool DisableBingResults { get; set; } = true;
        public bool DisableAppSuggestions { get; set; } = true;
        public bool TurnOffSystemSounds { get; set; } = false;

        // === ВКЛАДКА 4: УДАЛЕНИЕ ПРОГРАММ ===
        public bool ClearStartPins { get; set; } = true;
        public bool RemoveTeams { get; set; } = true;
        public bool RemoveOneDrive { get; set; } = true;
        public bool RemoveCortana { get; set; } = true;
        public bool RemoveClipchamp { get; set; } = true;
        public bool RemoveSolitaire { get; set; } = true;
        public bool RemoveStickyNotes { get; set; } = true;
        public bool RemoveTodos { get; set; } = true;
        public bool RemoveFeedbackHub { get; set; } = true;
        public bool RemoveGetHelp { get; set; } = true;
        public bool RemoveBingSearch { get; set; } = true;
        public bool RemoveOffice365 { get; set; } = true;
        public bool RemoveSkype { get; set; } = true;

        // Массив байтов готового XML
        public byte[]? GeneratedXmlBytes { get; private set; }

        public UnattendConfigWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            // --- БЕЗОПАСНАЯ ИНИЦИАЛИЗАЦИЯ ПОСЛЕ ПОЛНОЙ ЗАГРУЗКИ ОКНА ---
            this.Loaded += (s, e) =>
            {
                if (DialogNavigation != null && SecurityItem != null)
                {
                    // Нативно переходим на первую вкладку, при этом Wpf.Ui сам обработает активацию стиля кнопки!
                    DialogNavigation.Navigate("security");
                    UpdateActiveTabVisibility("security");
                }
            };
        }

        // --- КЛИК НА ВКЛАДКИ НАВИГАЦИИ ---
        private void NavigationViewItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is NavigationViewItem clickedItem && clickedItem.Tag is string tag)
            {
                // Сбрасываем активность со всех пунктов и ставим только выбранному
                SecurityItem.IsActive = (clickedItem == SecurityItem);
                PerformanceItem.IsActive = (clickedItem == PerformanceItem);
                InterfaceItem.IsActive = (clickedItem == InterfaceItem);
                DebloaterItem.IsActive = (clickedItem == DebloaterItem);

                UpdateActiveTabVisibility(tag);
            }
        }

        // --- ВСПОМОГАТЕЛЬНЫЙ МЕТОД ПЕРЕКЛЮЧЕНИЯ ВИДИМОСТИ ---
        private void UpdateActiveTabVisibility(string tag)
        {
            if (SecurityScroll == null || PerformanceScroll == null || InterfaceScroll == null || DebloaterScroll == null)
                return;

            SecurityScroll.Visibility = tag == "security" ? Visibility.Visible : Visibility.Collapsed;
            PerformanceScroll.Visibility = tag == "performance" ? Visibility.Visible : Visibility.Collapsed;
            InterfaceScroll.Visibility = tag == "interface" ? Visibility.Visible : Visibility.Collapsed;
            DebloaterScroll.Visibility = tag == "debloater" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var generator = new UnattendGenerator();

                // Сбор пакетов Debloater
                var bloatwares = new List<Bloatware>();
                if (RemoveTeams) TryAddBloatware(generator, bloatwares, "RemoveTeams");
                if (RemoveOneDrive) TryAddBloatware(generator, bloatwares, "RemoveOneDrive");
                if (RemoveCortana) TryAddBloatware(generator, bloatwares, "RemoveCopilot");
                if (RemoveClipchamp) TryAddBloatware(generator, bloatwares, "RemoveClipchamp");
                if (RemoveSolitaire) TryAddBloatware(generator, bloatwares, "RemoveSolitaire");
                if (RemoveStickyNotes) TryAddBloatware(generator, bloatwares, "RemoveStickyNotes");
                if (RemoveTodos) TryAddBloatware(generator, bloatwares, "RemoveTodos");
                if (RemoveFeedbackHub) TryAddBloatware(generator, bloatwares, "RemoveFeedbackHub");
                if (RemoveGetHelp) TryAddBloatware(generator, bloatwares, "RemoveGetHelp");
                if (RemoveBingSearch) TryAddBloatware(generator, bloatwares, "RemoveBingSearch");
                if (RemoveOffice365) TryAddBloatware(generator, bloatwares, "RemoveOffice365");
                if (RemoveSkype) TryAddBloatware(generator, bloatwares, "RemoveSkypeApp");

                // Перезапись иммутабельной конфигурации Кристофа через with-выражение
                Configuration config = Configuration.Default with
                {
                    // Системные обходы требований (IPESettings -> DefaultPESettings)
                    PESettings = new DefaultPESettings(BypassRequirementsCheck: BypassRequirementsCheck),

                    // Срок годности пароля (IPasswordExpirationSettings)
                    PasswordExpirationSettings = PasswordExpirationUnlimited
                        ? new UnlimitedPasswordExpirationSettings()
                        : new DefaultPasswordExpirationSettings(),

                    // Очистка закрепок меню Пуск (IStartPinsSettings)
                    StartPinsSettings = ClearStartPins
                        ? new EmptyStartPinsSettings()
                        : new DefaultStartPinsSettings(),

                    // Оформление интерфейса (IColorSettings)
                    ColorSettings = new CustomColorSettings(
                        SystemTheme: SystemColorThemeDark ? ColorTheme.Dark : ColorTheme.Light,
                        AppsTheme: AppsColorThemeDark ? ColorTheme.Dark : ColorTheme.Light,
                        EnableTransparency: true,
                        AccentColorOnStart: false,
                        AccentColorOnBorders: false,
                        AccentColor: System.Drawing.ColorTranslator.FromHtml("#DD68F1")
                    ),

                    // Скрытие/Показ файлов (None - все файлы наружу, HiddenSystem - прятать системные)
                    HideFiles = ShowHiddenFiles ? HideModes.None : HideModes.HiddenSystem,

                    // Прямые логические свойства ядра генерации
                    BypassNetworkCheck = BypassNetworkCheck,
                    DisableSmartScreen = DisableSmartScreen,
                    DisableSac = DisableSac,
                    DisableUac = DisableUac,
                    DisableCoreIsolation = DisableCoreIsolation,
                    DisableWpbt = DisableWpbt,

                    DisableWindowsUpdate = DisableWindowsUpdate,
                    DisableWidgets = DisableWidgets,
                    DisableSystemRestore = DisableSystemRestore,
                    DisableFastStartup = DisableFastStartup,
                    PreventDeviceEncryption = PreventDeviceEncryption,
                    DisablePointerPrecision = DisablePointerPrecision,
                    EnableRemoteDesktop = EnableRemoteDesktop,
                    EnableLongPaths = EnableLongPaths,
                    DeleteWindowsOld = DeleteWindowsOld,

                    ClassicContextMenu = ClassicContextMenu,
                    LeftTaskbar = AlignTaskbarLeft,
                    HideTaskViewButton = HideTaskViewButton,
                    ShowFileExtensions = ShowFileExtensions,
                    HideInfoTip = HideInfoTip,
                    ShowAllTrayIcons = ShowAllTrayIcons,
                    DisableBingResults = DisableBingResults,
                    DisableAppSuggestions = DisableAppSuggestions,
                    TurnOffSystemSounds = TurnOffSystemSounds,

                    // Оптимальные дефолты Edge
                    HideEdgeFre = true,
                    DisableEdgeStartupBoost = true,
                    DisableAutomaticRestartSignOn = true,

                    // Сформированный Debloat-список
                    Bloatwares = ImmutableList.CreateRange(bloatwares)
                };

                // Генерация XML документа
                XmlDocument xmlDoc = generator.GenerateXml(config);

                // Перевод документа в байты с ASCII/BOM кодированием [1]
                GeneratedXmlBytes = UnattendGenerator.Serialize(xmlDoc);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сборщика конфигурации:\n{ex.Message}", "Ошибка",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void TryAddBloatware(UnattendGenerator generator, List<Bloatware> list, string id)
        {
            try
            {
                var item = generator.Lookup<Bloatware>(id);
                if (item != null) list.Add(item);
            }
            catch { /* Игнорируем отсутствие специфических пакетов */ }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}