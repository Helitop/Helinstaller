using Helinstaller.BreachMiniGame;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Helinstaller.BreachMiniGame
{
    // Конвертер для отображения результата игры (Победа/Поражение)
    public class GameResultConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isWon)
            {
                return isWon ? "УСПЕХ" : "НЕУДАЧА";
            }
            return "ОЖИДАНИЕ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Конвертер для цвета результата игры
    public class GameResultColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isWon)
            {
                return isWon ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : new SolidColorBrush(Color.FromRgb(255, 0, 100)); // Неоново-зеленый или Неоново-красный
            }
            return new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Логика взаимодействия для CyberBreachProtocolControl.xaml
    /// </summary>
    public partial class CyberBreachProtocolControl : UserControl
    {
        private BreachProtocolGame Game { get; set; }

        public CyberBreachProtocolControl()
        {
            this.Resources.Add("BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
            this.Resources.Add("GameResultConverter", new GameResultConverter());
            this.Resources.Add("GameResultColorConverter", new GameResultColorConverter());

            InitializeComponent();

            Game = new BreachProtocolGame();
            this.DataContext = Game;

            BuildMatrixUI();
        }

        // Динамическое создание кнопок матрицы
        private void BuildMatrixUI()
        {
            // Очистка предыдущего состояния
            MatrixGrid.Children.Clear();
            MatrixGrid.RowDefinitions.Clear();
            MatrixGrid.ColumnDefinitions.Clear();

            int size = Game.Matrix.Count;

            // Создание определений строк и столбцов
            for (int i = 0; i < size; i++)
            {
                MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Создание и размещение кнопок
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    var cell = Game.Matrix[i][j];

                    var button = new Button
                    {
                        Content = cell.Code,
                        Style = (Style)this.FindResource("MatrixButtonStyle"),
                        DataContext = cell // Привязка данных к модели ячейки
                    };

                    // Установка позиции в Grid
                    Grid.SetRow(button, i);
                    Grid.SetColumn(button, j);

                    // Добавление обработчика клика
                    button.Click += MatrixCell_Click;

                    MatrixGrid.Children.Add(button);
                }
            }
        }

        // Обработка клика по ячейке матрицы
        private void MatrixCell_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is MatrixCell cell)
            {
                if (Game.SelectCell(cell))
                {
                    // Успешный выбор, логика UI обновляется через привязки (DataContext, ObservableObject)
                }
            }
        }

        // Обработка клика по кнопке перезапуска
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            Game.ResetGame();
            // Перестраиваем UI, так как модель MatrixCell была пересоздана
            BuildMatrixUI();
        }
    }
}