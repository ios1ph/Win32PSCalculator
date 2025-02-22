using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Win32PSCalculator
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string RegistryPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
        private const string RegistryKeyName = "Win32PrioritySeparation";

        private readonly List<(int decimalValue, string description)> _knownValues = new List<(int, string)>
        {
            (0x2A, "Short, Fixed, 3:1"),    // 42 decimal
            (0x29, "Short, Fixed, 2:1"),    // 41 decimal
            (0x28, "Short, Fixed, 1:1"),    // 40 decimal

            (0x1A, "Long, Fixed, 3:1"),     // 26 decimal
            (0x19, "Long, Fixed, 2:1"),     // 25 decimal
            (0x18, "Long, Fixed, 1:1"),     // 24 decimal

            (0x26, "Short, Variable, 3:1"), // 38 decimal
            (0x25, "Short, Variable, 2:1"), // 37 decimal
            (0x24, "Short, Variable, 1:1"), // 36 decimal

            (0x16, "Long, Variable, 3:1"),  // 22 decimal
            (0x15, "Long, Variable, 2:1"),  // 21 decimal
            (0x14, "Long, Variable, 1:1")   // 20 decimal
        };

        private readonly Dictionary<int, Button> _buttonsMap = new Dictionary<int, Button>();

        public MainWindow()
        {
            InitializeComponent();

            CreateKnownValueButtons();

            int currentRegistryValue = ReadRegistryValue();
            HighlightCurrentValue(currentRegistryValue);

            if (!_buttonsMap.ContainsKey(currentRegistryValue))
            {
                string hexStr = currentRegistryValue.ToString("X").PadLeft(8, '0');
                HexInputTextBox.Text = hexStr;
            }
        }

        private void CreateKnownValueButtons()
        {
            foreach (var (decValue, desc) in _knownValues)
            {
                var btn = new Button
                {
                    Style = (Style)FindResource("RoundedButtonStyle"),
                    Content = $"{desc}\n(0x{decValue:X} / {decValue} dec)",
                    Tag = decValue,
                    Cursor = Cursors.Hand
                };

                btn.Click += KnownValueButton_Click;
                QuantumButtonsPanel.Items.Add(btn);
                _buttonsMap[decValue] = btn;
            }
        }

        private void KnownValueButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int decValue)
            {
                WriteRegistryValue(decValue);
                HighlightCurrentValue(decValue);
                HexInputTextBox.Text = decValue.ToString("X").PadLeft(8, '0');
            }
        }

        private void HighlightCurrentValue(int currentValue)
        {
            foreach (var button in _buttonsMap.Values)
            {
                button.Background = Brushes.LightGray;
            }

            if (_buttonsMap.TryGetValue(currentValue, out Button selectedButton))
            {
                selectedButton.Background = Brushes.LightGreen;
            }
        }

        private int ReadRegistryValue()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryPath, false))
                {
                    if (key != null)
                    {
                        object valueObj = key.GetValue(RegistryKeyName);
                        if (valueObj != null)
                        {
                            return Convert.ToInt32(valueObj);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error reading registry: " + ex.Message);
            }
            return 0;
        }

        private void WriteRegistryValue(int newValue)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue(RegistryKeyName, newValue, RegistryValueKind.DWord);
                    }
                    else
                    {
                        MessageBox.Show("Could not open registry key for writing.");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error writing registry: " + ex.Message);
            }
        }

        private void CheckButton_Click(object sender, RoutedEventArgs e)
        {
            string input = HexInputTextBox.Text.Trim();

            if (Regex.IsMatch(input, @"\A\b[0-9a-fA-F]{1,8}\b\Z"))
            {
                int decimalValue = Convert.ToInt32(input, 16);
                string paddedHex = decimalValue.ToString("X").PadLeft(8, '0');
                HexInputTextBox.Text = paddedHex;
                HexOutputTextBox.Text = Calculate(paddedHex);
            }
            else
            {
                MessageBox.Show("Invalid hex input. Please enter up to 8 hex digits (0-9, A-F).");
            }
        }

        public string Calculate(string hexValue)
        {
            if (!uint.TryParse(hexValue, NumberStyles.HexNumber, null, out uint input))
            {
                return "Invalid hex input";
            }

            // Ядро читает только 6 бит из значения (маска 0x3F)
            uint effective = input & 0x3F; // эффективное значение от 0 до 63

            // Разбиваем 6 бит на 3 поля по 2 бита:
            // Биты [1:0] - соотношение квантума (boost)
            uint boostField = effective & 0x3;
            // Биты [3:2] - тип квантования (фиксированный/переменный)
            uint fixedField = (effective >> 2) & 0x3;
            // Биты [5:4] - интервал квантования (количество квантумов)
            uint quantumUnitField = (effective >> 4) & 0x3;

            // Определяем соотношение квантумов (PsPrioritySeparation)
            string boostRatio = boostField switch
            {
                0 => "1:1",
                1 => "2:1",
                2 => "3:1",
                3 => "3:1",
                _ => "Unknown"
            };

            // Определяем тип квантования (фиксированный/переменный)
            string fixedQuantum = fixedField switch
            {
                0 => "Variable",
                1 => "Variable",
                2 => "Fixed",
                3 => "Variable",
                _ => "Unknown"
            };

            // Определяем интервал квантования (Short/Long intervals)
            string quantumInterval = quantumUnitField switch
            {
                0 => "Short",
                1 => "Long",
                2 => "Short",
                3 => "Short",
                _ => "Unknown"
            };

            // Поиск в списке известных значений (SAME AS)
            int found = _knownValues.Find(x => x.description == quantumInterval + ", " + fixedQuantum + ", " + boostRatio).decimalValue;
            string sameAsLine = $"Same as: 0x{found:X2} ({found})";

            string result = $"Entered value: 0x{input:X8}\n" +
                            sameAsLine +
                            "\n\n" +
                            $"Effective 6-bit value: 0x{effective:X2} ({effective})\n\n" +
                            $"Quantum Interval (bits [5:4]): {quantumUnitField} -> {quantumInterval}\n" +
                            $"Quantum Type (bits [3:2]): {fixedField} -> {fixedQuantum}\n" +
                            $"PsPrioritySeparation (bits [1:0]): {boostField} -> Foreground boost: {boostRatio}";

            return result;
        }
    }
}
