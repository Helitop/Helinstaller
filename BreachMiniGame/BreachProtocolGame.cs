using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

namespace Helinstaller.BreachMiniGame
{
    // Базовый класс для реализации INotifyPropertyChanged
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // Класс для ячейки матрицы
    public class MatrixCell : ObservableObject
    {
        public string Code { get; set; }
        public int Row { get; set; }
        public int Col { get; set; }

        private bool _isCurrentSelectionRow;
        public bool IsCurrentSelectionRow
        {
            get => _isCurrentSelectionRow;
            set
            {
                if (_isCurrentSelectionRow != value)
                {
                    _isCurrentSelectionRow = value;
                    OnPropertyChanged(nameof(IsCurrentSelectionRow));
                }
            }
        }

        private bool _isCurrentSelectionCol;
        public bool IsCurrentSelectionCol
        {
            get => _isCurrentSelectionCol;
            set
            {
                if (_isCurrentSelectionCol != value)
                {
                    _isCurrentSelectionCol = value;
                    OnPropertyChanged(nameof(IsCurrentSelectionCol));
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
    }

    // Класс для целевой последовательности
    public class TargetSequence : ObservableObject
    {
        public string Name { get; set; } // Имя демона или награды
        public ObservableCollection<string> Codes { get; set; }
        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        // Для подсветки активных (не завершенных) последовательностей
        public bool IsActive => !IsCompleted;

        // Цвет статуса для UI
        public Brush StatusColor => IsCompleted ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : new SolidColorBrush(Color.FromRgb(255, 0, 100));
    }

    // Основная игровая логика
    public class BreachProtocolGame : ObservableObject
    {
        private const int MatrixSize = 5;
        // ИЗМЕНЕНИЕ 1: Буфер увеличен
        private const int DefaultBufferSize = 6;
        private readonly string[] AvailableCodes = { "1C", "E9", "55", "BD", "7A" };

        public ObservableCollection<ObservableCollection<MatrixCell>> Matrix { get; private set; }
        public ObservableCollection<TargetSequence> TargetSequences { get; private set; }
        public ObservableCollection<string> Buffer { get; private set; } = new ObservableCollection<string>();

        private int _bufferSize = DefaultBufferSize;
        public int BufferSize
        {
            get => _bufferSize;
            private set
            {
                _bufferSize = value;
                OnPropertyChanged(nameof(BufferSize));
                OnPropertyChanged(nameof(BufferStatus));
            }
        }

        public string BufferStatus => $"БУФЕР ({Buffer.Count}/{BufferSize})";

        private int _currentRow = -1;
        private bool _isSelectingRow = true; // Начинаем с выбора строки

        public BreachProtocolGame()
        {
            Matrix = new ObservableCollection<ObservableCollection<MatrixCell>>();
            TargetSequences = new ObservableCollection<TargetSequence>();
            InitializeGame();
        }

        // Инициализация игры: создание матрицы и целей
        public void InitializeGame()
        {
            // 1. Создание Матрицы
            Matrix.Clear();
            var random = new Random();
            for (int i = 0; i < MatrixSize; i++)
            {
                var row = new ObservableCollection<MatrixCell>();
                for (int j = 0; j < MatrixSize; j++)
                {
                    row.Add(new MatrixCell
                    {
                        Row = i,
                        Col = j,
                        Code = AvailableCodes[random.Next(AvailableCodes.Length)]
                    });
                }
                Matrix.Add(row);
            }

            // 2. ИЗМЕНЕНИЕ: Создание СЛУЧАЙНЫХ Последовательностей
            TargetSequences.Clear();

            int numberOfSequences = 3; // Сколько последовательностей создать
            int minSequenceLength = 2; // Минимальная длина
            int maxSequenceLength = 3; // Максимальная длина

            for (int i = 0; i < numberOfSequences; i++)
            {
                var newSequence = new TargetSequence
                {
                    Name = $"ДЕМОН #{i + 1}",
                    Codes = new ObservableCollection<string>()
                };

                // +1, т.к. верхняя граница Next() эксклюзивная
                int sequenceLength = random.Next(minSequenceLength, maxSequenceLength + 1);

                for (int j = 0; j < sequenceLength; j++)
                {
                    // Добавляем случайный код из доступных
                    newSequence.Codes.Add(AvailableCodes[random.Next(AvailableCodes.Length)]);
                }

                TargetSequences.Add(newSequence);
            }

            // 3. Сброс Буфера
            Buffer.Clear();
            _isSelectingRow = true;
            HighlightSelectionOptions();
        }

        // Выделение доступных для выбора ячеек (строка или столбец)
        private void HighlightSelectionOptions()
        {
            // Сброс всех выделений
            foreach (var row in Matrix)
            {
                foreach (var cell in row)
                {
                    cell.IsCurrentSelectionRow = false;
                    cell.IsCurrentSelectionCol = false;
                }
            }

            // Выделение новой строки/столбца
            if (_currentRow == -1) // Начало игры, выделяем всю первую строку
            {
                for (int j = 0; j < MatrixSize; j++)
                {
                    Matrix[0][j].IsCurrentSelectionRow = true;
                }
                _currentRow = 0;
            }
            else
            {
                if (_isSelectingRow)
                {
                    // Выделяем текущую строку
                    for (int j = 0; j < MatrixSize; j++)
                    {
                        Matrix[_currentRow][j].IsCurrentSelectionRow = true;
                    }
                }
                else
                {
                    // Выделяем текущий столбец
                    for (int i = 0; i < MatrixSize; i++)
                    {
                        Matrix[i][_currentRow].IsCurrentSelectionCol = true;
                    }
                }
            }
        }

        // Обработка клика по ячейке
        public bool SelectCell(MatrixCell cell)
        {
            // Проверка на заполненность буфера
            if (Buffer.Count >= BufferSize)
            {
                // Буфер заполнен, игра окончена
                return false;
            }

            // Проверка, можно ли выбрать эту ячейку (она должна быть выделена)
            if (!cell.IsCurrentSelectionRow && !cell.IsCurrentSelectionCol)
            {
                // Неправильный выбор
                return false;
            }

            // 1. Добавить код в буфер и пометить ячейку как выбранную
            Buffer.Add(cell.Code);
            cell.IsSelected = true;
            OnPropertyChanged(nameof(BufferStatus));

            // 2. Сброс предыдущих выделений
            foreach (var row in Matrix)
            {
                foreach (var c in row)
                {
                    c.IsCurrentSelectionRow = false;
                    c.IsCurrentSelectionCol = false;
                }
            }

            // 3. Переключение режима выбора (строка <-> столбец)
            _isSelectingRow = !_isSelectingRow;

            // Установка новой "текущей" позиции для следующего выбора
            if (_isSelectingRow)
            {
                // Был выбран столбец, теперь нужно выбрать строку: новая _currentRow = cell.Row
                _currentRow = cell.Row;
            }
            else
            {
                // Была выбрана строка, теперь нужно выбрать столбец: новая _currentRow = cell.Col
                _currentRow = cell.Col;
            }

            // 4. Проверка последовательностей
            CheckSequences();

            // 5. Выделение новых доступных ячеек
            if (Buffer.Count < BufferSize)
            {
                HighlightSelectionOptions();
            }
            else
            {
                // Буфер заполнен, игра окончена
                OnPropertyChanged(nameof(GameFinished));
                // Также нужно обновить GameWon, чтобы UI сразу показал "УСПЕХ"
                OnPropertyChanged(nameof(GameWon));
            }

            return true;
        }

        // Логика проверки (остается без изменений)
        private void CheckSequences()
        {
            var bufferList = Buffer.ToList();
            int bufferCount = bufferList.Count;

            foreach (var sequence in TargetSequences.Where(s => !s.IsCompleted))
            {
                var codeList = sequence.Codes.ToList();
                int sequenceLength = codeList.Count;

                // Проверка, что буфер достаточно длинный
                if (bufferCount < sequenceLength) continue;

                // Ищем совпадение, начиная с КАЖДОГО возможного индекса в буфере
                for (int i = 0; i <= bufferCount - sequenceLength; i++)
                {
                    // Берем под-последовательность из буфера
                    var bufferSubsequence = bufferList.Skip(i).Take(sequenceLength);

                    // Сравниваем
                    if (bufferSubsequence.SequenceEqual(codeList))
                    {
                        sequence.IsCompleted = true;
                        // Если нашли совпадение для этой (sequence),
                        // прерываем внутренний цикл (for) и переходим к следующей (foreach)
                        break;
                    }
                }
            }
        }

        // Свойство для проверки окончания игры
        public bool GameFinished => Buffer.Count >= BufferSize;

        // Победа засчитывается, если выполнена ХОТЯ БЫ ОДНА последовательность
        public bool GameWon => TargetSequences.Any(s => s.IsCompleted);

        // Сброс и начало новой игры
        public void ResetGame()
        {
            InitializeGame();
            OnPropertyChanged(nameof(GameFinished)); // Обновить статус UI
            OnPropertyChanged(nameof(GameWon));

            // Нужно снова вызвать OnPropertyChanged для BufferStatus
            OnPropertyChanged(nameof(BufferStatus));
        }
    }
}