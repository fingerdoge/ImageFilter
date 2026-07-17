using System;
using System.Windows;
using System.Windows.Threading;

namespace Imagefilter
{
    public partial class App : Application
    {
        public App()
        {
            // 订阅 UI 线程异常
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            // 订阅非 UI 线程异常
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"UI 异常：{e.Exception.Message}\n{e.Exception.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // 阻止程序崩溃
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show($"后台异常：{ex?.Message}\n{ex?.StackTrace}", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}