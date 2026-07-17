using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using ImageSharp = SixLabors.ImageSharp.Image;

namespace Imagefilter
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ImageInfo> _results = new();
        private readonly string _configPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ImageFilter", "config.json");

        private string _currentMode = "Normal";
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isUpdatingSlider = false;

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                // 强制显示窗口
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                this.Visibility = Visibility.Visible;
                this.Show();
                this.Activate();
                this.Topmost = true;
                System.Threading.Thread.Sleep(100);
                this.Topmost = false;

                listResults.ItemsSource = _results;
                LogMessage("程序已启动，准备就绪。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"窗口初始化错误：{ex.Message}\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadConfig();
                AttachCheckboxEvents();
                AttachNumericTextBoxEvents();
                SelectMode("Normal");

                // 在 Loaded 中初始化滑块（此时控件已完全创建）
                try
                {
                    UpdateSliderPair(NormalPixelSliderMin, NormalPixelSliderMax, NormalPixelRangeHighlight, NormalPixelRangeText);
                    UpdateSliderPair(WidePixelSliderMin, WidePixelSliderMax, WidePixelRangeHighlight, WidePixelRangeText);
                    UpdateSliderPair(AvatarPixelSliderMin, AvatarPixelSliderMax, AvatarPixelRangeHighlight, AvatarPixelRangeText);
                    UpdateSliderPair(PortraitPixelSliderMin, PortraitPixelSliderMax, PortraitPixelRangeHighlight, PortraitPixelRangeText);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"滑块初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载窗口时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== 模式切换 ==========
        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag is string mode)
                SelectMode(mode);
        }

        private void SelectMode(string mode)
        {
            _currentMode = mode;
            btnNormal.Background = mode == "Normal" ? (System.Windows.Media.Brush)FindResource("AccentColor") : System.Windows.Media.Brushes.Gray;
            btnWide.Background = mode == "Wide" ? (System.Windows.Media.Brush)FindResource("AccentColor") : System.Windows.Media.Brushes.Gray;
            btnAvatar.Background = mode == "Avatar" ? (System.Windows.Media.Brush)FindResource("AccentColor") : System.Windows.Media.Brushes.Gray;
            btnPortrait.Background = mode == "Portrait" ? (System.Windows.Media.Brush)FindResource("AccentColor") : System.Windows.Media.Brushes.Gray;

            panelNormal.Visibility = mode == "Normal" ? Visibility.Visible : Visibility.Collapsed;
            panelWide.Visibility = mode == "Wide" ? Visibility.Visible : Visibility.Collapsed;
            panelAvatar.Visibility = mode == "Avatar" ? Visibility.Visible : Visibility.Collapsed;
            panelPortrait.Visibility = mode == "Portrait" ? Visibility.Visible : Visibility.Collapsed;

            LogMessage($"切换到 {mode} 模式");
        }

        // ========== 配置管理 ==========
        private class Config
        {
            public string SourcePath { get; set; } = "";
            public string DestPath { get; set; } = "";
        }

        private void LoadConfig()
        {
            try
            {
                if (System.IO.File.Exists(_configPath))
                {
                    string json = System.IO.File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<Config>(json);
                    if (config != null)
                    {
                        txtSourcePath.Text = config.SourcePath;
                        txtDestPath.Text = config.DestPath;
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(txtSourcePath.Text)) txtSourcePath.Text = "";
            if (string.IsNullOrEmpty(txtDestPath.Text)) txtDestPath.Text = "";
        }

        private void SaveConfig()
        {
            try
            {
                var config = new Config
                {
                    SourcePath = txtSourcePath.Text,
                    DestPath = txtDestPath.Text
                };
                string json = JsonSerializer.Serialize(config);
                string dir = System.IO.Path.GetDirectoryName(_configPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(_configPath, json);
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => SaveConfig();

        // ========== 输入验证（实时修正） ==========
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsNumeric(e.Text);
        }
        private bool IsNumeric(string text) => text.All(char.IsDigit);

        private void NumericTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            string text = box.Text;
            if (string.IsNullOrEmpty(text)) return;

            string name = box.Name;
            bool isRatioBox = name == "txtNormalRatioW" || name == "txtNormalRatioH" ||
                              name == "txtWideRatioW" || name == "txtWideRatioH" ||
                              name == "txtPortraitRatioW" || name == "txtPortraitRatioH";

            // 去除前导零
            if (text.Length > 1 && text[0] == '0')
            {
                string trimmed = text.TrimStart('0');
                if (string.IsNullOrEmpty(trimmed)) trimmed = "0";
                box.Text = trimmed;
                box.CaretIndex = trimmed.Length;
                return;
            }

            if (int.TryParse(text, out int value))
            {
                if (isRatioBox)
                {
                    if (value < 1)
                    {
                        box.Text = "1";
                        box.CaretIndex = 1;
                        return;
                    }
                    if (value > 100)
                    {
                        box.Text = "100";
                        box.CaretIndex = 3;
                        return;
                    }
                }
                else
                {
                    if (value > 9999)
                    {
                        box.Text = "9999";
                        box.CaretIndex = 4;
                        return;
                    }
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            if (!string.IsNullOrEmpty(box.Text))
                box.Text = box.Text.Trim();
        }

        private void AttachNumericTextBoxEvents()
        {
            var allTextBoxes = FindVisualChildren<TextBox>(this);
            foreach (var tb in allTextBoxes)
            {
                string name = tb.Name;
                if (name.StartsWith("txt") && name != "txtSourcePath" && name != "txtDestPath" && name != "txtLog")
                {
                    tb.TextChanged += NumericTextBox_TextChanged;
                }
            }
        }

        // ========== 浏览文件夹 ==========
        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtSourcePath.Text = dialog.FolderName;
        }
        private void BrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtDestPath.Text = dialog.FolderName;
        }

        // ========== 清空列表 ==========
        private void ClearList_Click(object sender, RoutedEventArgs e)
        {
            _results.Clear();
            LogMessage("列表已清空。");
            statusBar.Text = "列表已清空。";
        }

        // ========== 停止筛选 ==========
        private void StopFilter_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            LogMessage("用户请求停止筛选...");
            statusBar.Text = "正在停止筛选...";
        }

        // ========== 开关联动 ==========
        private void AttachCheckboxEvents()
        {
            var allCheckboxes = FindVisualChildren<CheckBox>(this);
            foreach (var chk in allCheckboxes)
            {
                if (chk.Name.StartsWith("chk") && !chk.Name.Contains("Invert") && chk.Name != "chkRecursive")
                {
                    chk.Checked -= EnableDisable_Checked;
                    chk.Unchecked -= EnableDisable_Checked;
                    chk.Checked += EnableDisable_Checked;
                    chk.Unchecked += EnableDisable_Checked;
                    UpdateTextBoxEnabledState(chk);
                }
            }
        }

        private void EnableDisable_Checked(object sender, RoutedEventArgs e)
        {
            var chk = sender as CheckBox;
            if (chk == null) return;
            UpdateTextBoxEnabledState(chk);
        }

        private void UpdateTextBoxEnabledState(CheckBox chk)
        {
            string name = chk.Name;

            // 普通模式比例
            if (name == "chkNormalRatio")
            {
                var txtW = FindName("txtNormalRatioW") as TextBox;
                var txtH = FindName("txtNormalRatioH") as TextBox;
                bool isChecked = chk.IsChecked == true;
                if (txtW != null) { txtW.IsEnabled = isChecked; txtW.Text = ""; }
                if (txtH != null) { txtH.IsEnabled = isChecked; txtH.Text = ""; }
                var invertChk = FindName("chkNormalRatioInvert") as CheckBox;
                if (invertChk != null) { invertChk.IsEnabled = isChecked; if (!isChecked) invertChk.IsChecked = false; }
                return;
            }
            // 宽屏比例
            if (name == "chkWideRatio")
            {
                var txtW = FindName("txtWideRatioW") as TextBox;
                var txtH = FindName("txtWideRatioH") as TextBox;
                bool isChecked = chk.IsChecked == true;
                if (txtW != null) { txtW.IsEnabled = isChecked; txtW.Text = ""; }
                if (txtH != null) { txtH.IsEnabled = isChecked; txtH.Text = ""; }
                var invertChk = FindName("chkWideRatioInvert") as CheckBox;
                if (invertChk != null) { invertChk.IsEnabled = isChecked; if (!isChecked) invertChk.IsChecked = false; }
                return;
            }
            // 竖屏比例
            if (name == "chkPortraitRatio")
            {
                var txtW = FindName("txtPortraitRatioW") as TextBox;
                var txtH = FindName("txtPortraitRatioH") as TextBox;
                bool isChecked = chk.IsChecked == true;
                if (txtW != null) { txtW.IsEnabled = isChecked; txtW.Text = ""; }
                if (txtH != null) { txtH.IsEnabled = isChecked; txtH.Text = ""; }
                var invertChk = FindName("chkPortraitRatioInvert") as CheckBox;
                if (invertChk != null) { invertChk.IsEnabled = isChecked; if (!isChecked) invertChk.IsChecked = false; }
                return;
            }

            // 普通像素开关
            string targetName = name.Replace("chk", "txt");
            var txt = FindName(targetName) as TextBox;
            if (txt != null)
            {
                txt.IsEnabled = chk.IsChecked == true;
                if (chk.IsChecked == false)
                {
                    if (targetName.Contains("Max") || targetName.Contains("max"))
                        txt.Text = "9999";
                    else if (targetName.Contains("Min") || targetName.Contains("min"))
                        txt.Text = "0";
                }
                else
                {
                    txt.Text = "";
                }
            }
        }

        private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) yield return t;
                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        // ========== 像素值滑块事件处理 ==========
        private void NormalPixelSliderMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(NormalPixelSliderMin, NormalPixelSliderMax, NormalPixelRangeHighlight, NormalPixelRangeText);
        }
        private void NormalPixelSliderMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(NormalPixelSliderMin, NormalPixelSliderMax, NormalPixelRangeHighlight, NormalPixelRangeText);
        }

        private void WidePixelSliderMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(WidePixelSliderMin, WidePixelSliderMax, WidePixelRangeHighlight, WidePixelRangeText);
        }
        private void WidePixelSliderMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(WidePixelSliderMin, WidePixelSliderMax, WidePixelRangeHighlight, WidePixelRangeText);
        }

        private void AvatarPixelSliderMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(AvatarPixelSliderMin, AvatarPixelSliderMax, AvatarPixelRangeHighlight, AvatarPixelRangeText);
        }
        private void AvatarPixelSliderMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(AvatarPixelSliderMin, AvatarPixelSliderMax, AvatarPixelRangeHighlight, AvatarPixelRangeText);
        }

        private void PortraitPixelSliderMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(PortraitPixelSliderMin, PortraitPixelSliderMax, PortraitPixelRangeHighlight, PortraitPixelRangeText);
        }
        private void PortraitPixelSliderMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderPair(PortraitPixelSliderMin, PortraitPixelSliderMax, PortraitPixelRangeHighlight, PortraitPixelRangeText);
        }

        // 核心更新方法（加入空值检查）
        private void UpdateSliderPair(Slider minSlider, Slider maxSlider, System.Windows.Shapes.Rectangle highlight, TextBlock textBlock)
        {
            // 空值检查，防止控件未加载时调用
            if (minSlider == null || maxSlider == null || highlight == null || textBlock == null)
                return;

            if (_isUpdatingSlider) return;
            _isUpdatingSlider = true;

            try
            {
                double min = minSlider.Value;
                double max = maxSlider.Value;
                double step = 500000;

                if (min >= max)
                {
                    minSlider.Value = max - step;
                }
                if (max <= min)
                {
                    maxSlider.Value = min + step;
                }

                min = minSlider.Value;
                max = maxSlider.Value;

                var parent = highlight.Parent as Grid;
                if (parent != null)
                {
                    double trackWidth = parent.ActualWidth;
                    if (trackWidth > 0)
                    {
                        double minPercent = (min - minSlider.Minimum) / (minSlider.Maximum - minSlider.Minimum);
                        double maxPercent = (max - maxSlider.Minimum) / (maxSlider.Maximum - maxSlider.Minimum);
                        double marginLeft = trackWidth * minPercent;
                        double width = trackWidth * (maxPercent - minPercent);
                        highlight.Margin = new Thickness(marginLeft, 0, 0, 0);
                        highlight.Width = width;
                    }
                }

                textBlock.Text = $"{FormatNumber((int)min)} - {FormatNumber((int)max)} px";
            }
            finally
            {
                _isUpdatingSlider = false;
            }
        }

        private string FormatNumber(int num)
        {
            if (num >= 1000000)
                return (num / 1000000.0).ToString("0.##") + "M";
            else if (num >= 1000)
                return (num / 1000.0).ToString("0.#") + "K";
            else
                return num.ToString();
        }

        // ========== 开始筛选 ==========
        private async void StartFilter_Click(object sender, RoutedEventArgs e)
        {
            // 比例阈值相等验证（宽高相同）
            if (_currentMode == "Normal" && chkNormalRatio.IsChecked == true)
            {
                int rw = ParseOrDefault(txtNormalRatioW.Text, -1);
                int rh = ParseOrDefault(txtNormalRatioH.Text, -1);
                if (rw > 0 && rh > 0 && rw == rh)
                {
                    MessageBox.Show("1:1图片请选择头像模式（1:1）喵", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else if (_currentMode == "Wide" && chkWideRatio.IsChecked == true)
            {
                int rw = ParseOrDefault(txtWideRatioW.Text, -1);
                int rh = ParseOrDefault(txtWideRatioH.Text, -1);
                if (rw > 0 && rh > 0 && rw == rh)
                {
                    MessageBox.Show("1:1图片请选择头像模式（1:1）喵", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else if (_currentMode == "Portrait" && chkPortraitRatio.IsChecked == true)
            {
                int rw = ParseOrDefault(txtPortraitRatioW.Text, -1);
                int rh = ParseOrDefault(txtPortraitRatioH.Text, -1);
                if (rw > 0 && rh > 0 && rw == rh)
                {
                    MessageBox.Show("1:1图片请选择头像模式（1:1）喵", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            // 取消旧任务
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            _results.Clear();

            try
            {
                var sourcePath = txtSourcePath.Text.Trim();
                if (!System.IO.Directory.Exists(sourcePath))
                {
                    MessageBox.Show("源路径不存在，请重新选择。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool recursive = chkRecursive.IsChecked == true;
                var fileList = GetImageFiles(sourcePath, recursive);
                LogMessage($"扫描到 {fileList.Count} 个图片文件。");
                statusBar.Text = $"扫描到 {fileList.Count} 个图片文件。";

                if (fileList.Count == 0)
                {
                    MessageBox.Show($"路径 '{sourcePath}' 下没有找到支持的图片文件。\n支持扩展名：.jpg .jpeg .png .bmp .gif .tiff .webp", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                statusBar.Text = "正在分析图片尺寸，请稍候...";
                LogMessage("开始筛选...");

                var progress = new Progress<string>(msg =>
                {
                    statusBar.Text = msg;
                    LogMessage(msg);
                });

                var parameters = ReadModeParameters(_currentMode);
                var token = _cancellationTokenSource.Token;

                var result = await Task.Run(() => FilterImages(fileList, _currentMode, parameters, progress, token), token);

                foreach (var item in result)
                    _results.Add(item);

                string finalMsg = $"筛选完成，共找到 {_results.Count} 张符合条件的图片。";
                statusBar.Text = finalMsg;
                LogMessage(finalMsg);

                if (_results.Count == 0 && fileList.Count > 0)
                {
                    MessageBox.Show("筛选完成，但未匹配任何图片。请检查筛选条件是否合理，或查看日志中的错误信息。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("筛选已被用户取消。");
                statusBar.Text = "筛选已取消。";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"发生错误：{ex.Message}\n\n{ex.StackTrace}", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"错误：{ex.Message}");
                statusBar.Text = "出错，请查看日志。";
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        // ========== 读取模式参数 ==========
        private ModeParameters ReadModeParameters(string mode)
        {
            var p = new ModeParameters();
            var clarityRanges = new List<(int Low, int High)>();

            if (mode == "Normal")
            {
                p.EnableMaxW = chkNormalMaxW.IsChecked == true;
                p.MaxW = ParseOrDefault(txtNormalMaxW.Text, 9999);
                p.EnableMinW = chkNormalMinW.IsChecked == true;
                p.MinW = ParseOrDefault(txtNormalMinW.Text, 0);
                p.EnableMaxH = chkNormalMaxH.IsChecked == true;
                p.MaxH = ParseOrDefault(txtNormalMaxH.Text, 9999);
                p.EnableMinH = chkNormalMinH.IsChecked == true;
                p.MinH = ParseOrDefault(txtNormalMinH.Text, 0);

                p.EnableRatio = chkNormalRatio.IsChecked == true;
                if (p.EnableRatio)
                {
                    int rw = ParseOrDefault(txtNormalRatioW.Text, -1);
                    int rh = ParseOrDefault(txtNormalRatioH.Text, -1);
                    if (rw > 0 && rh > 0)
                    {
                        p.RatioThreshold = (double)rw / rh;
                        p.RatioInvert = chkNormalRatioInvert.IsChecked == true;
                    }
                    else
                    {
                        p.EnableRatio = false;
                    }
                }

                if (NormalClaritySeg0.IsChecked == true) clarityRanges.Add((1, 720));
                if (NormalClaritySeg1.IsChecked == true) clarityRanges.Add((720, 1080));
                if (NormalClaritySeg2.IsChecked == true) clarityRanges.Add((1080, 1440));
                if (NormalClaritySeg3.IsChecked == true) clarityRanges.Add((1440, 2160));
                if (NormalClaritySeg4.IsChecked == true) clarityRanges.Add((2160, int.MaxValue));

                p.EnablePixelRange = true;
                p.PixelRangeMin = (int)NormalPixelSliderMin.Value;
                p.PixelRangeMax = (int)NormalPixelSliderMax.Value;
            }
            else if (mode == "Wide")
            {
                p.EnableMaxW = chkWideMaxW.IsChecked == true;
                p.MaxW = ParseOrDefault(txtWideMaxW.Text, 9999);
                p.EnableMinW = chkWideMinW.IsChecked == true;
                p.MinW = ParseOrDefault(txtWideMinW.Text, 0);
                p.EnableMaxH = chkWideMaxH.IsChecked == true;
                p.MaxH = ParseOrDefault(txtWideMaxH.Text, 9999);
                p.EnableMinH = chkWideMinH.IsChecked == true;
                p.MinH = ParseOrDefault(txtWideMinH.Text, 0);

                p.EnableRatio = chkWideRatio.IsChecked == true;
                if (p.EnableRatio)
                {
                    int rw = ParseOrDefault(txtWideRatioW.Text, -1);
                    int rh = ParseOrDefault(txtWideRatioH.Text, -1);
                    if (rw > 0 && rh > 0)
                    {
                        p.RatioThreshold = (double)rw / rh;
                        p.RatioInvert = chkWideRatioInvert.IsChecked == true;
                    }
                    else
                    {
                        p.EnableRatio = false;
                    }
                }

                if (WideClaritySeg0.IsChecked == true) clarityRanges.Add((1, 720));
                if (WideClaritySeg1.IsChecked == true) clarityRanges.Add((720, 1080));
                if (WideClaritySeg2.IsChecked == true) clarityRanges.Add((1080, 1440));
                if (WideClaritySeg3.IsChecked == true) clarityRanges.Add((1440, 2160));
                if (WideClaritySeg4.IsChecked == true) clarityRanges.Add((2160, int.MaxValue));

                p.EnablePixelRange = true;
                p.PixelRangeMin = (int)WidePixelSliderMin.Value;
                p.PixelRangeMax = (int)WidePixelSliderMax.Value;
            }
            else if (mode == "Avatar")
            {
                p.EnableMaxSize = chkAvatarMax.IsChecked == true;
                p.MaxSize = ParseOrDefault(txtAvatarMax.Text, 9999);
                p.EnableMinSize = chkAvatarMin.IsChecked == true;
                p.MinSize = ParseOrDefault(txtAvatarMin.Text, 0);
                p.EnableRatio = false;

                if (AvatarClaritySeg0.IsChecked == true) clarityRanges.Add((1, 720));
                if (AvatarClaritySeg1.IsChecked == true) clarityRanges.Add((720, 1080));
                if (AvatarClaritySeg2.IsChecked == true) clarityRanges.Add((1080, 1440));
                if (AvatarClaritySeg3.IsChecked == true) clarityRanges.Add((1440, 2160));
                if (AvatarClaritySeg4.IsChecked == true) clarityRanges.Add((2160, int.MaxValue));

                p.EnablePixelRange = true;
                p.PixelRangeMin = (int)AvatarPixelSliderMin.Value;
                p.PixelRangeMax = (int)AvatarPixelSliderMax.Value;
            }
            else if (mode == "Portrait")
            {
                p.EnableMaxW = chkPortraitMaxW.IsChecked == true;
                p.MaxW = ParseOrDefault(txtPortraitMaxW.Text, 9999);
                p.EnableMinW = chkPortraitMinW.IsChecked == true;
                p.MinW = ParseOrDefault(txtPortraitMinW.Text, 0);
                p.EnableMaxH = chkPortraitMaxH.IsChecked == true;
                p.MaxH = ParseOrDefault(txtPortraitMaxH.Text, 9999);
                p.EnableMinH = chkPortraitMinH.IsChecked == true;
                p.MinH = ParseOrDefault(txtPortraitMinH.Text, 0);

                p.EnableRatio = chkPortraitRatio.IsChecked == true;
                if (p.EnableRatio)
                {
                    int rw = ParseOrDefault(txtPortraitRatioW.Text, -1);
                    int rh = ParseOrDefault(txtPortraitRatioH.Text, -1);
                    if (rw > 0 && rh > 0)
                    {
                        p.RatioThreshold = (double)rw / rh;
                        p.RatioInvert = chkPortraitRatioInvert.IsChecked == true;
                    }
                    else
                    {
                        p.EnableRatio = false;
                    }
                }

                if (PortraitClaritySeg0.IsChecked == true) clarityRanges.Add((1, 720));
                if (PortraitClaritySeg1.IsChecked == true) clarityRanges.Add((720, 1080));
                if (PortraitClaritySeg2.IsChecked == true) clarityRanges.Add((1080, 1440));
                if (PortraitClaritySeg3.IsChecked == true) clarityRanges.Add((1440, 2160));
                if (PortraitClaritySeg4.IsChecked == true) clarityRanges.Add((2160, int.MaxValue));

                p.EnablePixelRange = true;
                p.PixelRangeMin = (int)PortraitPixelSliderMin.Value;
                p.PixelRangeMax = (int)PortraitPixelSliderMax.Value;
            }

            p.EnableClarity = clarityRanges.Count > 0;
            p.ClarityRanges = clarityRanges;
            return p;
        }

        private int ParseOrDefault(string text, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(text)) return defaultValue;
            if (int.TryParse(text, out int value)) return Math.Clamp(value, 0, 9999);
            return defaultValue;
        }

        // ========== 筛选逻辑 ==========
        private List<ImageInfo> FilterImages(List<string> files, string mode, ModeParameters p, IProgress<string> progress, CancellationToken token)
        {
            var matched = new List<ImageInfo>();
            int total = files.Count;
            int processed = 0;
            int errorCount = 0;

            progress?.Report($"共 {total} 个文件，开始处理...");

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                processed++;
                try
                {
                    using var image = ImageSharp.Load(file);
                    int w = image.Width;
                    int h = image.Height;
                    int longSide = Math.Max(w, h);
                    int shortSide = Math.Min(w, h);
                    double ratio = (double)w / h;
                    int totalPixels = w * h;

                    bool match = true;

                    if (mode == "Normal")
                    {
                        if (p.EnableMaxW && w > p.MaxW) match = false;
                        if (p.EnableMinW && w < p.MinW) match = false;
                        if (p.EnableMaxH && h > p.MaxH) match = false;
                        if (p.EnableMinH && h < p.MinH) match = false;
                        if (match && p.EnableRatio)
                        {
                            bool ratioMatch = Math.Abs(ratio - p.RatioThreshold) < 0.001;
                            if (p.RatioInvert) ratioMatch = !ratioMatch;
                            if (!ratioMatch) match = false;
                        }
                    }
                    else if (mode == "Wide")
                    {
                        if (w <= h) match = false;
                        else
                        {
                            if (p.EnableMaxW && w > p.MaxW) match = false;
                            if (p.EnableMinW && w < p.MinW) match = false;
                            if (p.EnableMaxH && h > p.MaxH) match = false;
                            if (p.EnableMinH && h < p.MinH) match = false;
                            if (match && p.EnableRatio)
                            {
                                bool ratioMatch = (ratio > 1 && ratio <= p.RatioThreshold);
                                if (p.RatioInvert) ratioMatch = !ratioMatch;
                                if (!ratioMatch) match = false;
                            }
                        }
                    }
                    else if (mode == "Avatar")
                    {
                        const double eps = 0.02;
                        if (Math.Abs(ratio - 1.0) > eps) match = false;
                        else
                        {
                            if (p.EnableMaxSize && longSide > p.MaxSize) match = false;
                            if (p.EnableMinSize && longSide < p.MinSize) match = false;
                        }
                    }
                    else if (mode == "Portrait")
                    {
                        if (w >= h) match = false;
                        else
                        {
                            if (p.EnableMaxW && w > p.MaxW) match = false;
                            if (p.EnableMinW && w < p.MinW) match = false;
                            if (p.EnableMaxH && h > p.MaxH) match = false;
                            if (p.EnableMinH && h < p.MinH) match = false;
                            if (match && p.EnableRatio)
                            {
                                bool ratioMatch = (ratio >= p.RatioThreshold && ratio < 1);
                                if (p.RatioInvert) ratioMatch = !ratioMatch;
                                if (!ratioMatch) match = false;
                            }
                        }
                    }

                    // 清晰度筛选（基于短边）
                    if (match && p.EnableClarity)
                    {
                        bool clarityOk = p.ClarityRanges.Any(range => shortSide >= range.Low && shortSide < range.High);
                        if (!clarityOk) match = false;
                    }

                    // 像素值范围筛选（总像素）—— 左包含右不包含
                    if (match && p.EnablePixelRange)
                    {
                        if (totalPixels < p.PixelRangeMin || totalPixels >= p.PixelRangeMax)
                            match = false;
                    }

                    if (match)
                    {
                        matched.Add(new ImageInfo
                        {
                            FileName = System.IO.Path.GetFileName(file),
                            FullPath = file,
                            Width = w,
                            Height = h,
                            LongSide = longSide,
                            ShortSide = shortSide,
                            Ratio = Math.Round(ratio, 3),
                            TotalPixels = totalPixels
                        });
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    System.Diagnostics.Debug.WriteLine($"读取失败 [{errorCount}]: {file} - {ex.Message}");
                }

                if (processed % 10 == 0 || processed == total)
                {
                    progress?.Report($"处理 {processed}/{total}，匹配 {matched.Count}，失败 {errorCount}");
                }
            }

            progress?.Report($"完成：匹配 {matched.Count}，失败 {errorCount}");
            return matched;
        }

        // ========== 获取图片文件列表 ==========
        private List<string> GetImageFiles(string root, bool recursive)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp"
            };
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return System.IO.Directory.GetFiles(root, "*.*", searchOption)
                            .Where(f => extensions.Contains(System.IO.Path.GetExtension(f)))
                            .ToList();
        }

        // ========== 复制 / 移动 ==========
        private async void CopyToDest_Click(object sender, RoutedEventArgs e) => await CopyOrMoveFiles(true);
        private async void MoveToDest_Click(object sender, RoutedEventArgs e) => await CopyOrMoveFiles(false);

        private async Task CopyOrMoveFiles(bool copy)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show("列表为空，请先执行筛选。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var destDir = txtDestPath.Text.Trim();
            if (!System.IO.Directory.Exists(destDir))
            {
                try { System.IO.Directory.CreateDirectory(destDir); }
                catch (Exception ex) { MessageBox.Show($"无法创建目标目录：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            }

            int success = 0, fail = 0;
            statusBar.Text = $"正在{(copy ? "复制" : "移动")}文件...";
            LogMessage($"开始{(copy ? "复制" : "移动")} {_results.Count} 个文件到 {destDir}");

            await Task.Run(() =>
            {
                var items = _results.ToList();
                foreach (var item in items)
                {
                    try
                    {
                        string destFile = System.IO.Path.Combine(destDir, item.FileName);
                        int count = 1;
                        string name = System.IO.Path.GetFileNameWithoutExtension(item.FileName);
                        string ext = System.IO.Path.GetExtension(item.FileName);
                        while (System.IO.File.Exists(destFile))
                            destFile = System.IO.Path.Combine(destDir, $"{name} ({count++}){ext}");

                        if (copy)
                            System.IO.File.Copy(item.FullPath, destFile);
                        else
                            System.IO.File.Move(item.FullPath, destFile);
                        success++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        Dispatcher.Invoke(() => LogMessage($"操作失败: {item.FileName} - {ex.Message}"));
                    }
                }
            });

            string finalMsg = $"操作完成：成功 {success} 个，失败 {fail} 个。";
            statusBar.Text = finalMsg;
            LogMessage(finalMsg);
            MessageBox.Show($"{(copy ? "复制" : "移动")}完成！\n成功：{success}\n失败：{fail}", "结果", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ========== 日志记录 ==========
        private void LogMessage(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {msg}{Environment.NewLine}");
                txtLog.ScrollToEnd();
            });
        }
    }

    public class ModeParameters
    {
        public bool EnableMaxW { get; set; }
        public int MaxW { get; set; }
        public bool EnableMinW { get; set; }
        public int MinW { get; set; }
        public bool EnableMaxH { get; set; }
        public int MaxH { get; set; }
        public bool EnableMinH { get; set; }
        public int MinH { get; set; }
        public bool EnableRatio { get; set; }
        public double RatioThreshold { get; set; }
        public bool RatioInvert { get; set; }
        public bool EnableMaxSize { get; set; }
        public int MaxSize { get; set; }
        public bool EnableMinSize { get; set; }
        public int MinSize { get; set; }
        public bool EnableClarity { get; set; }
        public List<(int Low, int High)> ClarityRanges { get; set; } = new();
        public bool EnablePixelRange { get; set; }
        public int PixelRangeMin { get; set; }
        public int PixelRangeMax { get; set; }
    }

    public class ImageInfo
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int LongSide { get; set; }
        public int ShortSide { get; set; }
        public double Ratio { get; set; }
        public int TotalPixels { get; set; }
    }
}