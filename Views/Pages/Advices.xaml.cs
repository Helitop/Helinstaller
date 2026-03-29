using System.Collections.ObjectModel;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Helinstaller.Views.Pages;

public record AdviceItem(SymbolRegular Icon, string Title, string Description);

public partial class Advices : Page
{
    public ObservableCollection<AdviceItem> AdviceItems { get; set; } = new();

    public Advices()
    {
        InitializeComponent();
        DataContext = this;
        LoadAdvices();
    }

    private void LoadAdvices()
    {
        AdviceItems.Add(new AdviceItem(
            SymbolRegular.ClipboardPaste24,
            "Буфер обмена",
            "Нажми WIN + V. Система покажет историю всего, что ты копировал. Больше не нужно копировать-вставлять по одному файлу."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.TasksApp28,
            "Диспетчер задач",
            "CTRL + SHIFT + ESC — это самый быстрый способ убить зависшее приложение. Открывается мгновенно."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.ShieldCheckmark24,
            "Про антивирусы",
            "Защитника Windows хватает. Сторонние антивирусы в наше время — это легальные вирусы, которые просто жрут твою оперативку."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Delete24,
            "Яндекс.Браузер",
            "Не ставь его, умоляю. Он тащит за собой кучу мусора, кнопок, Алису и прочую ерунду, которую потом замучаешься вычищать."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Earth24,
            "Браузеры",
            "Edge — топчик, он на движке Хрома, но быстрее. Если не он, то Chrome. Firefox — если ты любишь настраивать всё под себя."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.System24,
            "Переустановка",
            "Иногда винда так засирается, что проще переустановить её с нуля за 15 минут, чем искать, почему она грузится полчаса."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Key24,
            "Лицензия Windows",
            "Купленный ключ за 10к не защитит от лагов. Это просто цифры в реестре. Активируй и не парься, на работу системы это не влияет."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Delete24,
            "CCleaner и Ко",
            "Весь софт для 'очистки ОЗУ' и реестра — это плацебо. Если комп старый, его спасёт только апгрейд, а не чистка 'мусора'."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.CloudArrowUp24,
            "Бэкапы",
            "Диски умирают внезапно. Храни фотки и важные доки в облаке или на флешке в тумбочке. Я так однажды потерял всё."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Flash24,
            "Автозагрузка",
            "Зайди в Диспетчер задач -> Автозагрузка. Отключи всё, что тебе не нужно сразу при включении. Комп скажет спасибо."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.ArrowCounterclockwise24,
            "Перезагрузка",
            "Большинство глюков лечится перезагрузкой. Помни: 'Завершение работы' в Win11 — это не выключение, это сон. Жми 'Перезагрузка'."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Screenshot24,
            "Скриншоты",
            "WIN + SHIFT + S — и ты можешь вырезать любую часть экрана. Забудь про кнопку PrintScreen и Paint."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.WeatherMoon24,
            "Береги глаза",
            "Используй 'Ночной свет' в настройках экрана вечером. Синий свет мешает спать, а желтоватый оттенок реально спасает глаза."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Storage24,
            "Место на диске",
            "Если диск C забит под завязку (горит красным), винда начнет тупить. Оставляй хотя бы 20-30 ГБ свободными для кэша."));

        AdviceItems.Add(new AdviceItem(
            SymbolRegular.Keyboard24,
            "F2 — это сила",
            "Выдели файл и нажми F2, чтобы сразу его переименовать. Не нужно дважды медленно кликать мышкой."));
    }
}