using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool communityBusy;

	/// <summary>
	/// Writes the Online page's account line and returns this PC's card for the saved server, if any. cardProblem is
	/// the Online Status sentence when the card cannot be read.
	/// </summary>
	private string? ShowOnlineAccount(CommunityServer.Settings community, out string? cardProblem)
	{
		cardProblem = null;
		string? card = null;
		if (community.Host.Length > 0)
		{
			try
			{
				card = CommunityServer.ReadCard(GamePaths.GameRoot, community.Host);
			}
			catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
			{
				cardProblem = "อ่านบัญชีสำหรับ " + community.Host + " ไม่ได้: " + ex.Message;
			}
		}
		string? name = (card != null) ? CommunityServer.ReadMasterName(GamePaths.GameRoot, community.Host) : null;
		OnlineAccountText.Text = cardProblem ?? ((community.Host.Length == 0) ? "บันทึกที่อยู่เซิร์ฟเวอร์ด้านล่างก่อน" : (card == null) ? ("ยังไม่มีบัญชีบน " + community.Host + " เลย") : (name != null) ? (name + ", ลงท้ายด้วย " + card.Substring(16) + " บน " + community.Host) : ("ลงท้ายด้วย " + card.Substring(16) + " บน " + community.Host));
		return card;
	}

	private async void OnlineNewAccount_OnClick(object sender, RoutedEventArgs e)
	{
		await CommunityAccountAsync(recover: false);
	}

	private async void OnlineRecoverAccount_OnClick(object sender, RoutedEventArgs e)
	{
		await CommunityAccountAsync(recover: true);
	}

	/// <summary>New or Recover Account on the saved community server; refused from Play Online's start until the game ends.</summary>
	private async Task CommunityAccountAsync(bool recover)
	{
		// Both change the card Play Online is starting or running with, and the server refuses the old one from then on.
		if (playStarting || CommunityTunnelRunning)
		{
			ThemedMessageBox.Show("หยุดเกมก่อน: บัญชีใหม่หรือบัญชีที่กู้คืนจะเปลี่ยนรหัสที่เกมใช้เล่น และเกมจะเสียการเชื่อมต่อ", recover ? "กู้คืนบัญชี" : "บัญชีใหม่", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		CommunityServer.Settings saved = SavedServer();
		// The card's folder is named after the address, so a hand-edited one is checked before any card is read.
		try
		{
			_ = CommunityServer.NormalizeHost(saved.Host);
		}
		catch (ArgumentException ex)
		{
			ThemedMessageBox.Show((saved.Host.Length == 0) ? "โหมดออนไลน์ต้องใช้ที่อยู่ของเซิร์ฟเวอร์ชุมชน บันทึกไว้ใต้หัวข้อ เซิร์ฟเวอร์ชุมชน ในหน้านี้ก่อน" : ex.Message, recover ? "กู้คืนบัญชี" : "บัญชีใหม่", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (recover)
		{
			await RecoverAccountAsync(saved);
		}
		else
		{
			await NewAccountAsync(saved);
		}
		await RefreshRuntimeStatusAsync();
		await ProbeCommunityServerAsync();
	}

	/// <summary>True when Play Online can go ahead: this PC holds an account for the saved server, or has just made one.</summary>
	private async Task<bool> EnsureCommunityAccountAsync()
	{
		CommunityServer.Settings settings = SavedServer();
		try
		{
			if (CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host) != null)
			{
				return true;
			}
		}
		catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(ex.Message, "เซิร์ฟเวอร์ชุมชน", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		if (ThemedMessageBox.Show("เครื่องนี้ยังไม่มีบัญชีบนเซิร์ฟเวอร์ชุมชน " + settings.Host + "\n\nสร้างบัญชีใหม่ตอนนี้เลยไหม หากคุณมีบัญชีอยู่แล้ว ให้เลือก ไม่ใช่ แล้วใช้ กู้คืนบัญชี ในหน้าออนไลน์พร้อมคีย์กู้คืนของคุณ", "เซิร์ฟเวอร์ชุมชน", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
		{
			return false;
		}
		return await CreateCommunityAccountAsync(settings);
	}

	private async Task<bool> CreateCommunityAccountAsync(CommunityServer.Settings settings)
	{
		// The dialog sends the request itself, so a refused name keeps it open for another.
		CommunityAccountDialog dialog = new CommunityAccountDialog(this, settings.Host, (string name, bool sendMachineId) => CommunityServer.CreateAccountAsync(settings, name, sendMachineId));
		if (DialogSink.ShowDialog(dialog) != true || dialog.Account == null)
		{
			if (dialog.Failure != null)
			{
				ThemedMessageBox.Show(dialog.Failure, "บัญชีใหม่", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
			return false;
		}
		CommunityServer.Account account = dialog.Account;
		return await TakeCommunityAccountAsync(settings, () => Task.FromResult(account), "บัญชีใหม่", recovered: false);
	}

	/// <summary>Runs the account request, stores the card it brings and shows its recovery key once.</summary>
	private async Task<bool> TakeCommunityAccountAsync(CommunityServer.Settings settings, Func<Task<CommunityServer.Account>> request, string caption, bool recovered)
	{
		if (communityBusy)
		{
			return false;
		}
		communityBusy = true;
		OnlineNewAccountButton.IsEnabled = false;
		OnlineRecoverAccountButton.IsEnabled = false;
		try
		{
			// The key is shown even when the card cannot be stored: it is then the only copy of the account.
			(CommunityServer.Account account, string? storeError) = await CommunityServer.TakeAccountAsync(GamePaths.GameRoot, settings.Host, request());
			DialogSink.ShowDialog(new RecoveryKeyDialog(this, settings.Host, account.RecoveryKey, recovered));
			if (storeError != null)
			{
				ThemedMessageBox.Show((recovered ? "บัญชีถูกกู้คืนแล้ว" : "บัญชีถูกสร้างแล้ว") + " แต่เครื่องนี้เก็บบัญชีไม่ได้: " + storeError + "\n\nแก้ปัญหานั้นก่อน แล้วใช้ กู้คืนบัญชี กับคีย์ที่คุณเพิ่งบันทึกไว้", caption, MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return false;
			}
			return true;
		}
		catch (IOException ex2)
		{
			ThemedMessageBox.Show(ex2.Message, caption, MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
		finally
		{
			communityBusy = false;
			OnlineNewAccountButton.IsEnabled = true;
			OnlineRecoverAccountButton.IsEnabled = true;
		}
	}

	/// <summary>New Account on the given saved settings: asks before replacing an account this PC already holds.</summary>
	private async Task NewAccountAsync(CommunityServer.Settings settings)
	{
		string? card;
		try
		{
			card = CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(ex.Message, "บัญชีใหม่", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		if (card != null && ThemedMessageBox.Show("เครื่องนี้มีบัญชีบน " + settings.Host + " อยู่แล้ว บัญชีใหม่จะมาแทนที่บัญชีนั้นในเครื่องนี้ และบัญชีเดิมจะกลับมาได้ก็ต่อเมื่อใช้คีย์กู้คืนของมันเท่านั้น\n\nสร้างบัญชีใหม่ต่อไปหรือไม่", "บัญชีใหม่", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		await CreateCommunityAccountAsync(settings);
	}

	/// <summary>Recover Account on the given saved settings; refused while the game runs through the tunnel.</summary>
	private async Task RecoverAccountAsync(CommunityServer.Settings settings)
	{
		if (CommunityTunnelRunning)
		{
			ThemedMessageBox.Show("หยุดเกมก่อน: การกู้คืนบัญชีจะเปลี่ยนรหัสของบัญชี และเกมที่กำลังทำงานอยู่จะเสียการเชื่อมต่อ", "กู้คืนบัญชี", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string? card;
		try
		{
			card = CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			ThemedMessageBox.Show(ex.Message, "กู้คืนบัญชี", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		// Recovery rotates the code and the key on the server, so the card here is replaced whatever it holds.
		if (card != null && ThemedMessageBox.Show("เครื่องนี้มีบัญชีบน " + settings.Host + " อยู่แล้ว บัญชีที่กู้คืนจะมาแทนที่บัญชีนั้นในเครื่องนี้ หากเป็นบัญชีอื่น ก็จะกลับมาได้เฉพาะเมื่อใช้คีย์กู้คืนของบัญชีนั้นเอง\n\nกู้คืนต่อไปหรือไม่", "กู้คืนบัญชี", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		RecoverAccountDialog dialog = new RecoverAccountDialog(this, settings.Host);
		if (DialogSink.ShowDialog(dialog) != true)
		{
			return;
		}
		await TakeCommunityAccountAsync(settings, () => CommunityServer.RecoverAccountAsync(settings, dialog.RecoveryKey), "กู้คืนบัญชี", recovered: true);
	}
}
