using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool refreshingStatus;

	private bool lastKnownGameRunning;

	private DateTime statusHoldUntil = DateTime.MinValue;

	/// <summary>A status sentence the player should get to read before the two-second tick replaces it.</summary>
	private void HoldStatus(string text, bool problem = false)
	{
		RuntimeStatusText.Text = text;
		SetStatusTone(problem ? "problem" : "next");
		statusHoldUntil = DateTime.UtcNow.AddSeconds(12.0);
	}

	/// <summary>Colours the dot beside the status sentence: ready, busy, problem, or a next step to take.</summary>
	private void SetStatusTone(string tone)
	{
		StatusDot.Fill = ToneBrush(tone);
	}

	private Brush ToneBrush(string tone)
	{
		return (Brush)FindResource(tone switch
		{
			"ready" => "IceBrush",
			"busy" => "InfoBrush",
			"problem" => "DangerBrush",
			_ => "WarnBrush",
		});
	}

	private async Task RefreshRuntimeStatusAsync()
	{
		if (refreshingStatus || windowClosing)
		{
			return;
		}
		refreshingStatus = true;
		try
		{
			bool gameRunning = (lastKnownGameRunning = IsThisGameRunning());
			CommunityServer.Settings community = SavedServer();
			ShowMasterLine();
			string? card = ShowOnlineAccount(community, out string? cardProblem);
			// The local server's own ports, asked while the game is idle: they name the Next step too.
			bool[]? serverPorts = null;
			if (!gameRunning && !launcherProcessRunning)
			{
				serverPorts = await Task.WhenAll(ServerSettingsView.ConfiguredPorts().Take(3).Select(IsPortOpenAsync));
			}
			string text;
			string tone;
			NextStep next = NextStep.None;
			if (startupChecking)
			{
				text = "กำลังตรวจสอบโฟลเดอร์เกม ปุ่มเล่นจะพร้อมในอีกสักครู่";
				tone = "busy";
			}
			else if (savedReport != null)
			{
				text = "บันทึกรายงานแล้ว: " + savedReport + " แนบไฟล์นี้ในโพสต์ของคุณบน Discord";
				tone = "ready";
				next = NextStep.OpenFolder;
			}
			else if (installProblem != null)
			{
				text = installProblem;
				tone = "problem";
			}
			else if (gameRunning)
			{
				text = "เกมกำลังทำงานอยู่ ปุ่มหยุดเกมจะปิดเกมและปิดหน้าต่างของเกม";
				tone = "ready";
			}
			else if (launcherProcessRunning)
			{
				text = "เกมกำลังเริ่มต้น หน้าจอไตเติลจะปรากฏในประมาณหนึ่งนาที";
				tone = "busy";
			}
			else if (playProblem != null)
			{
				text = playProblem;
				tone = "problem";
			}
			else if (whatsNewBody != null)
			{
				text = "อัปเดตเป็น " + UpdateSettings.Version + " ดูรายการสิ่งที่เปลี่ยนได้ที่ปุ่มมีอะไรใหม่";
				tone = "ready";
				next = NextStep.WhatsNew;
			}
			else if (availableUpdate != null && !updateDismissed)
			{
				text = "มีอัปเดต " + availableUpdate.Version + " ให้ติดตั้งแล้ว ปุ่มติดตั้งจะติดตั้งให้ แล้วตัวเรียกเกมจะรีสตาร์ตเพื่อทำให้เสร็จสมบูรณ์";
				tone = "next";
				next = NextStep.Install;
			}
			else
			{
				bool[] source = serverPorts ?? Array.Empty<bool>();
				bool anyAccount = (AccountComboBox.ItemsSource as IEnumerable<AccountEntry>)?.Any() == true;
				bool currentAccount = (AccountComboBox.ItemsSource as IEnumerable<AccountEntry>)?.Any((AccountEntry account) => account.IsCurrent) == true;
				(text, tone) = localToolMissing ? (CommunityServer.Offered ? "สำเนานี้ไม่มีเซิร์ฟเวอร์ในเครื่อง เล่นออนไลน์ได้ในหน้าออนไลน์ หรือแตกไฟล์แพ็กเกจเกมฉบับเต็ม" : "สำเนานี้ไม่มีเซิร์ฟเวอร์ในเครื่อง แตกไฟล์แพ็กเกจเกมฉบับเต็ม", "problem")
					: (!anyAccount && !firstRunning) ? ("ยังไม่มีบัญชีในเครื่อง เลือก บัญชีใหม่ ในเมนู Master แล้วกดเล่น", "next")
					: (!currentAccount && !firstRunning) ? ("ยังไม่ได้เลือก Master เลือกจากเมนู Master แล้วกดเล่น", "next")
					: (cardCollection.SelectedCards.Count == 0) ? ("เด็คว่างเปล่า เลือกการ์ดได้สูงสุด 30 ใบในหน้าการ์ด แล้วกดเล่น", "next")
					: source.All((bool value) => value) ? ("เซิร์ฟเวอร์ในเครื่องกำลังทำงาน กดเล่นได้เลย", "ready")
					: source.Any((bool value) => value) ? ("เซิร์ฟเวอร์ในเครื่องตอบสนองบางพอร์ตแต่ไม่ครบทุกพอร์ต กดหยุดเซิร์ฟเวอร์ แล้วกดเล่น", "problem")
					: ("พร้อมแล้ว การกดเล่นจะเริ่มเซิร์ฟเวอร์ในเครื่องก่อน", "ready");
			}
			// Start or Stop server by hand whenever Local is idle and nothing else is the next step.
			if (next == NextStep.None && serverPorts != null && !localToolMissing && !startupChecking && installProblem == null)
			{
				next = serverPorts.Any((bool value) => value) ? NextStep.StopServer : NextStep.StartServer;
			}
			bool busy = serverConfiguring || stoppingServer || updateInstalling || windowClosing || firstRunning;
			if (!busy && DateTime.UtcNow >= statusHoldUntil)
			{
				RuntimeStatusText.Text = text;
				SetStatusTone(tone);
			}
			ShowNextStep((busy || playStarting) ? NextStep.None : next);
			StatusProgress.Visibility = (startupChecking || firstRunning || serverConfiguring || stoppingServer || updateInstalling || (playStarting && !sessionOnline) || (launcherProcessRunning && !gameRunning && !sessionOnline)) ? Visibility.Visible : Visibility.Hidden;
			StartGameButton.IsEnabled = !gameRunning && !launcherProcessRunning && !busy && !startupChecking && installProblem == null && !localToolMissing;
			StopGameButton.IsEnabled = gameRunning || launcherProcessRunning;
			ShowOnlinePage(gameRunning, busy, community, card, cardProblem);
			SetAccountControlsEnabled(!accountToolRunning);
		}
		finally
		{
			refreshingStatus = false;
		}
	}

	private static async Task<bool> IsPortOpenAsync(int port)
	{
		using TcpClient client = new TcpClient();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500.0));
		try
		{
			await client.ConnectAsync("127.0.0.1", port, timeout.Token);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
