using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Windows
{
    public partial class HelpDialog : FluentWindow
    {
        public record HelpItem(string ElementName, string WhatItDoes, string Advice, Wpf.Ui.Controls.SymbolRegular Icon);

        public HelpDialog(string pageKey)
        {
            InitializeComponent();
            LoadHelpForPage(pageKey);
        }

        private void LoadHelpForPage(string pageKey)
        {
            HelpCardsContainer.Children.Clear();
            List<HelpItem> items = new();

            switch (pageKey.ToLower())
            {
                // ==========================================
                // 1. ГЛАВНАЯ (ДАТЧИКИ И СИСТЕМА)
                // ==========================================
                case "systemdashboard":
                case "systemdashboardpage":
                    PageTitle.Text = "Главная — Состояние компьютера";
                    PageIcon.Symbol = SymbolRegular.Desktop24;
                    items = new List<HelpItem>
                    {
                        new("Характеристики системы", "Показывает точную модель твоего процессора, видеокарты и версию Windows.", "Полезно знать, когда качаешь игры или драйверы, чтобы не гадать, какое у тебя железо.", SymbolRegular.Info24),
                        new("Загрузка ЦП (Процессор)", "Показывает, насколько сейчас напряжен мозг компьютера.", "Если в покое держится 90-100% — значит винда что-то обновляет, либо какой-то процесс или вирус жрет ресурсы в фоне.", SymbolRegular.BrainCircuit24),
                        new("Память ОЗУ (Оперативка)", "Сколько гигабайт памяти занято открытыми программами и вкладками браузера.", "Если забито под завязку (95%+) — закрой лишние вкладки или тяжелые программы, иначе всё начнет дико фризить.", SymbolRegular.Memory16),
                        new("Загрузка ГП (Видеокарта)", "Показывает нагрузку на графический чип.", "На рабочем столе должно быть около 0-5%. В играх 95-100% — это норма (значит видеокарта работает на всю катушку).", SymbolRegular.DeveloperBoard24),
                        new("Сеть (Трафик)", "Текущая скорость приема и отдачи данных через интернет.", "Если ты ничего не качаешь, а счетчик выдает мегабайты — винда качает обновления или фоновые программы что-то синхронизируют.", SymbolRegular.NetworkAdapter16),
                        new("Локальные накопители (Диски)", "Сколько места осталось на дисках C:, D: и т.д.", "Золотое правило: на диске C: ВСЕГДА держи минимум 15–20 ГБ свободными, иначе Windows начнет адски тупить.", SymbolRegular.HardDrive24)
                    };
                    break;

                // ==========================================
                // 2. ПРИЛОЖЕНИЯ (DASHBOARD)
                // ==========================================
                case "dashboard":
                case "dashboardpage":
                    PageTitle.Text = "Приложения — Центр установки софта";
                    PageIcon.Symbol = SymbolRegular.Apps24;
                    items = new List<HelpItem>
                    {
                        new("Поисковая строка вверху", "Ищет любые программы по всей мировой базе пакетов WinGet.", "Вбивай название на английском (например, Telegram, Steam, Blender). Нажал Enter — получил список.", SymbolRegular.Search24),
                        new("Кнопка «Поиск обновлений»", "Сканирует твои установленные программы на наличие новых версий.", "Жми раз в неделю. Если вышли апдейты — появится зеленая кнопка «Обновить всё».", SymbolRegular.ArrowCounterclockwise24),
                        new("Кнопка «Обновить всё (N)»", "Автоматически по очереди обновляет весь твой устаревший софт.", "Нажал один раз — и ушел пить чай. Программа сама тихо обновит всё без лишних вопросов.", SymbolRegular.ArrowDownload24),
                        new("Карточка программы", "Клик по любой карточке открывает выпадающее окно управления.", "Там написан точный вес файла, официальный источник и кнопка быстрой установки.", SymbolRegular.AppGeneric24),
                        new("Тумблер «Принудительно»", "Заставляет WinGet переустановить программу заново с ключом --force.", "Включай, если программа уже установлена, но заглючила, повредилась или ты хочешь накатить поверх свежую копию.", SymbolRegular.ArrowRepeatAll24)
                    };
                    break;

                // ==========================================
                // 3. ТВSideКИ (TWEAKS)
                // ==========================================
                case "tweaks":
                    PageTitle.Text = "Твики — Оптимизация и настройка Windows";
                    PageIcon.Symbol = SymbolRegular.Wrench24;
                    items = new List<HelpItem>
                    {
                        new("Активация Windows / Office", "Цифровая вечная лицензия через официальный открытый метод MAS.", "Безопасно навсегда. Не слетает при обновлениях, не требует сторонних вирусов и KMS-серверов.", SymbolRegular.WindowShield24),
                        new("Активация WinRAR", "Вшивает лицензионный ключ в папку программы.", "Навсегда убирает назойливое всплывающее окно «Срок пробного периода истек, купите лицензию».", SymbolRegular.Archive24),
                        new("Контроль учетных записей (UAC)", "Тот самый тумблер всплывающих затемняющих окон с вопросом «Разрешить приложению...».", "Если выключить — окна пропадут, но включай только если уверен в том, что скачиваешь. Требует перезагрузки!", SymbolRegular.Shield24),
                        new("Брандмауэр Windows (Firewall)", "Встроенный сетевой фильтр защиты системы.", "Выключай только для сетевых тестов. В обычной жизни лучше держать включенным.", SymbolRegular.ShieldGlobe24),
                        new("Завершение в панели задач", "Добавляет пункт «Завершить задачу» по правому клику мыши на иконку в панели задач.", "Мега-удобно: зависла игра или браузер — кликнул правой кнопкой мыши и мгновенно убил процесс без Диспетчера задач.", SymbolRegular.Desktop24),
                        new("Дезинфекция (Anti-Yandex)", "Сносит региональную Яндекс.Музыку, глушит промо-софт и ставит Google в Edge.", "Переключает системный регион на США (язык остается русским), чтобы винда не качала скрытую рекламу.", SymbolRegular.Delete24),
                        new("Xbox DNS (Обход блокировок)", "Специальный быстрый Smart DNS сервер.", "Открывает прямой доступ к ChatGPT, Claude, сервисам Xbox Live и серверам без запуска стороннего VPN.", SymbolRegular.Globe24),
                        new("Вкладки Edge в Alt+Tab", "Очищает меню переключения окон по сочетанию Alt+Tab.", "Включай обязательно: убирает кашу из десятков вкладок браузера, оставляя только сами открытые окна.", SymbolRegular.Tab24),
                        new("Залипание клавиш", "Отключает пищащее системное окно при пятикратном нажатии клавиши Shift.", "Маст-хэв для геймеров, чтобы игра не сворачивалась посреди катки от частого бега/приседаний.", SymbolRegular.Keyboard24)
                    };
                    break;

                // ==========================================
                // 4. VENTOY (УСТАНОВКА WINDOWS)
                // ==========================================
                case "ventoy":
                    PageTitle.Text = "Установка Windows — Загрузочная флешка";
                    PageIcon.Symbol = SymbolRegular.WindowSettings20;
                    items = new List<HelpItem>
                    {
                        new("1. Выбор накопителя", "Выпадающий список твоих подключенных флешек.", "ВНИМАНИЕ: выбери правильную букву диска! Все старые файлы с флешки при установке Ventoy сотрутся.", SymbolRegular.UsbStick24),
                        new("2. Кнопка «Установить Ventoy»", "Записывает на флешку специальную мультизагрузочную разметку.", "Делается один раз в жизни! После этого форматировать флешку для новых виндов больше никогда не придется.", SymbolRegular.ArrowDownload24),
                        new("3. Кнопка «Обновить Ventoy»", "Обновляет версию загрузчика на флешке.", "Обновляет ядро Ventoy БЕЗ удаления образов и файлов, которые уже лежат на флешке.", SymbolRegular.ArrowSync24),
                        new("4. Поиск оригинальных образов", "Открывает проверенную официальную базу дистрибутивов Windows (Massgrave).", "Там лежат чистые оригинальные ISO-образы от Microsoft без мусора и вирусов.", SymbolRegular.Globe24),
                        new("5. Запись образа на накопитель", "Копирует выбранный ISO/IMG файл в специальную папку /ISO/ на флешке.", "Также можно просто вручную копировать ISO-файлы на флешку как обычные фильмы через Проводник.", SymbolRegular.FolderZip24),
                        new("6. Автоустановка (OOBE)", "Генерирует умный файл autounattend.xml для автоматической установки винды.", "Позволяет установить Windows 11 на старые ПК (без TPM и SecureBoot), отключает привязку учетки Microsoft и создает локальный профиль.", SymbolRegular.Wrench24)
                    };
                    break;

                // ==========================================
                // 5. ПАРАМЕТРЫ (SETTINGS)
                // ==========================================
                case "settings":
                case "settingspage":
                    PageTitle.Text = "Параметры — Настройка Helinstaller";
                    PageIcon.Symbol = SymbolRegular.Settings24;
                    items = new List<HelpItem>
                    {
                        new("Тема оформления", "Переключает визуальный стиль приложения (Светлая / Тёмная).", "Рекомендуется тёмная: она бережет глаза и выглядит более контрастно.", SymbolRegular.WeatherMoon24),
                        new("Авто-воспроизведение музыки", "Включает музыку сразу при открытии программы.", "Если выключить — плеер загрузит плейлист, но не начнет играть, пока ты сам не нажмешь Play.", SymbolRegular.MusicNote224),
                        new("Аудио-визуализатор", "Анимация прыгающего текста и басов под музыку.", "Если у тебя очень старый слабый ноутбук, можно выключить для экономии каждого процента процессора.", SymbolRegular.SoundSource24),
                        new("Таймаут зависания установщиков", "Сколько секунд ждать ответа от программы перед принудительным закрытием.", "По умолчанию 30 сек. Если интернет очень медленный, можно сдвинуть ползунок на 60–120 секунд.", SymbolRegular.Timer24),
                        new("Логирование событий", "Кнопка «Открыть файл логов».", "Если что-то пошло не так — открой лог и отправь его разработчику для быстрого исправления ошибок.", SymbolRegular.DocumentText24)
                    };
                    break;
            }

            // Генерируем карточки в UI
            foreach (var item in items)
            {
                HelpCardsContainer.Children.Add(CreateHelpCard(item));
            }
        }

        private UIElement CreateHelpCard(HelpItem item)
        {
            var card = new Wpf.Ui.Controls.Card
            {
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Иконка
            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = item.Icon,
                FontSize = 22,
                Foreground = (Brush)FindResource("AccentTextFillColorPrimaryBrush"),
                Margin = new Thickness(0, 2, 14, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // Текстовый блок
            var stack = new StackPanel();

            var title = new System.Windows.Controls.TextBlock
            {
                Text = item.ElementName,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 3)
            };

            var desc = new System.Windows.Controls.TextBlock
            {
                Text = item.WhatItDoes,
                FontSize = 12,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var advice = new System.Windows.Controls.TextBlock
            {
                Text = $"💡 Совет: {item.Advice}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 204, 0)), // Желтая подсказка
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 15,
                Opacity = 0.95
            };

            stack.Children.Add(title);
            stack.Children.Add(desc);
            stack.Children.Add(advice);

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            card.Content = grid;
            return card;
        }

        public static void ShowHelp(Window owner, string pageKey)
        {
            var dialog = new HelpDialog(pageKey)
            {
                Owner = owner
            };
            dialog.ShowDialog();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}