using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Yubico.YubiKey;
using Yubico.YubiKey.Oath;
using HashAlgorithm = Yubico.YubiKey.Oath.HashAlgorithm;

namespace AutoTf.YubiKeyInteractor;

public partial class MainWindow : Window
{
	private string? _token;
	private string? _proxyTokenName;
	private string? _proxyTokenValue;
	
	public MainWindow()
	{
		InitializeComponent();
	}
	
	private void CheckForKey_Click(object? sender, RoutedEventArgs e)
	{
		IYubiKeyDevice? device = YubiKeyDevice.FindAll().FirstOrDefault();
		if (device == null)
			return;

		FirmwareText.Text = device.FirmwareVersion.ToString();
		SerialNumberText.Text = device.SerialNumber.ToString();
		
		IndexApps_Click();
	}

	private void IndexApps_Click()
	{
		IYubiKeyDevice? device = YubiKeyDevice.FindAll().FirstOrDefault();
		if (device == null)
			return;

		using OathSession session = new OathSession(device);
		PasswordProtectedText.Text = session.IsPasswordProtected.ToString();
		Credentials.ItemsSource = session.GetCredentials().Select(x => x.AccountName);
	}

	private async void RegisterKey_Click(object? sender, RoutedEventArgs e)
	{
		IYubiKeyDevice? device = YubiKeyDevice.FindAll().FirstOrDefault();
		if (device == null)
			return;

		string secret = GenerateRandomString() + GenerateRandomString();
		using (OathSession session = new OathSession(device))
		{
			Credential cred = new Credential("AutoTF", "ATF-" + GenerateRandomString() + "-" + device.SerialNumber, CredentialType.Totp, HashAlgorithm.Sha256, secret, CredentialPeriod.Period15, 6, 0, false);
			
			session.AddCredential(cred);
		}
		
		using (HttpClient client = new HttpClient())
		{
			string requestUrl = $"https://{evuDomainText.Text}.server.autotf.de/sync/keys/addkey/?serialNumber={device.SerialNumber}&secret={secret}";

			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

			request.Headers.Add("Cookie", $"authentik_csrf={_token};{_proxyTokenValue!.Replace(", ", "=").Replace('[', ' ').Replace(']', ' ').Trim()}");
			HttpResponseMessage response = await client.SendAsync(request);

			InfoText.Text = response.IsSuccessStatusCode ? "Key synced successfully." : $"Failed to sync key. Status: {response.StatusCode}";
		}
		
		IndexApps_Click();
	}
	
	public static string GenerateRandomString()
	{
		Random random = new Random();
		const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
		return new string(Enumerable.Repeat(chars, 10)
			.Select(s => s[random.Next(s.Length)]).ToArray());
	}

	private void Login_Click(object? sender, RoutedEventArgs e)
	{
		evuInfo.IsVisible = false;
		LoginButton.IsVisible = false;
		StartWebServer();
		InfoText.Text = "Please navigate to /token";
	}

	private async void StartWebServer()
	{
		string url = "http://localhost:5000/token/";
		using HttpListener listener = new HttpListener();
		
		listener.Prefixes.Add(url);
		listener.Start();
		Console.WriteLine($"Listening for incoming requests at {url}...");

		OpenBrowser("https://" + evuDomainText.Text + ".server.autotf.de/token");
		HttpListenerContext context = await listener.GetContextAsync();
		HttpListenerRequest request = context.Request;

		_token = request.QueryString["csrf_token"];
		_proxyTokenValue = request.QueryString["proxy_token"];
		_proxyTokenName = request.QueryString["proxy_name"];
		if (!string.IsNullOrEmpty(_token) && !string.IsNullOrEmpty(_proxyTokenValue) && !string.IsNullOrEmpty(_proxyTokenName))
		{
			
			HttpListenerResponse response = context.Response;
			string responseString = "<html><body>Logged In. You can close this window.</body></html>";
			byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
			response.ContentLength64 = buffer.Length;
			await using (Stream output = response.OutputStream)
			{
				await output.WriteAsync(buffer, 0, buffer.Length);
			}

			InfoText.Text = "Logged In";
			listener.Stop();
			CheckBtn.IsEnabled = true;
			RegisterBtn.IsEnabled = true;
			ValidateBtn.IsEnabled = true;
		}
		else
		{
			Console.WriteLine("No token received.");
			evuInfo.IsVisible = true;
			LoginButton.IsVisible = true;
		}
	}

	private static void OpenBrowser(string url)
	{
		try
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
			{
				Process.Start("xdg-open", url);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				Process.Start("open", url);
			}
			else
			{
				throw new PlatformNotSupportedException("Unsupported platform.");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to open URL: {ex.Message}");
		}
	}

	private async void VaidateBtn_OnClick(object? sender, RoutedEventArgs e)
	{
		IYubiKeyDevice? device = YubiKeyDevice.FindAll().FirstOrDefault();
		if (device == null)
			return;

		string? code = null;
		DateTime timestamp = default;
		using (OathSession session = new OathSession(device))
		{
			foreach (Credential credential in session.GetCredentials())
			{
				if (credential.Issuer != "AutoTF")
					return;
				code = session.CalculateCredential(credential).Value!;
				timestamp = DateTime.UtcNow;
			}
		}

		if (code == null)
			InfoText.Text = "Could not find AutoTF key.";
		
		using (HttpClient client = new HttpClient())
		{
			string requestUrl = $"https://{evuDomainText.Text}.server.autotf.de/sync/keys/validate/?serialNumber={device.SerialNumber}&code={code}&timestamp={timestamp.ToString("yyyy-MM-ddTHH:mm:ss")}";

			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

			request.Headers.Add("Cookie", $"authentik_csrf={_token};{_proxyTokenValue!.Replace(", ", "=").Replace('[', ' ').Replace(']', ' ').Trim()}");
			HttpResponseMessage response = await client.SendAsync(request);

			InfoText.Text = response.IsSuccessStatusCode ? "Key validated successfully." : $"Failed to validate key. Status: {response.StatusCode}";
		}
	}
}