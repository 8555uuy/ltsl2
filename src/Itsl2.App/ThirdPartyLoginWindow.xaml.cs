using System.Net.Http;
using System.Windows;
using Itsl2.Core.Models;
using Itsl2.Core.Services;

namespace Itsl2.App;

public partial class ThirdPartyLoginWindow : Window
{
    private readonly YggdrasilAuthService _authService = new();

    public ThirdPartyAccount? Account { get; private set; }

    public ThirdPartyLoginWindow()
    {
        InitializeComponent();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        StatusText.Text = "正在连接皮肤站...";
        try
        {
            Account = await _authService.AuthenticateAsync(
                ServerUrlBox.Text,
                UsernameBox.Text,
                PasswordBox.Password);
            StatusText.Text = $"登录成功：{Account.ProfileName}";
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = $"网络请求失败：{ex.Message}";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "请求超时，请检查地址和网络后重试";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}