using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private AccountEntry? SelectedAccount => AccountComboBox.SelectedItem as AccountEntry;

	private void SetAccountControlsEnabled(bool enabled)
	{
		AccountComboBox.IsEnabled = enabled;
		bool flag = enabled && !lastKnownGameRunning;
		UseAccountButton.IsEnabled = flag;
		CreateAccountButton.IsEnabled = flag;
		ResetAccountButton.IsEnabled = flag;
		AccountUpgradePanel.IsEnabled = flag && SelectedAccount != null;
		DeleteAccountButton.IsEnabled = flag && SelectedAccount != null;
		RepairAccountButton.IsEnabled = flag;
		AccountsPage.ExportAccountsButton.IsEnabled = flag;
		AccountsPage.ImportAccountsButton.IsEnabled = flag;
		GrantBenefitsButton.IsEnabled = enabled && SelectedAccount != null;
		RefreshAccountsButton.IsEnabled = enabled;
		SummonHistoryButton.IsEnabled = enabled && SelectedAccount != null;
		OwnedCardsOnlyCheckBox.IsEnabled = enabled && SelectedAccount != null;
	}

	private void SelectAccountById(int aimeId)
	{
		if (AccountComboBox.ItemsSource is IEnumerable<AccountEntry> source)
		{
			AccountEntry accountEntry = source.FirstOrDefault((AccountEntry account) => account.AimeId == aimeId);
			if (accountEntry != null)
			{
				AccountComboBox.SelectedItem = accountEntry;
			}
		}
	}

	private void AccountComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		AccountEntry selectedAccount = SelectedAccount;
		RefreshAccountDetails(selectedAccount);
		SummonHistoryButton.IsEnabled = selectedAccount != null;
		GrantBenefitsButton.IsEnabled = selectedAccount != null && !accountToolRunning;
		AccountUpgradePanel.IsEnabled = selectedAccount != null && !lastKnownGameRunning && !accountToolRunning;
		DeleteAccountButton.IsEnabled = selectedAccount != null && !lastKnownGameRunning && !accountToolRunning;
		OwnedCardsOnlyCheckBox.IsEnabled = selectedAccount != null;
		ApplyOwnedCardFilter();
		CurrentSummonSummaryText.Text = ((selectedAccount == null) ? "Lv.1 - EXP 0 - ซัมมอน 0 ครั้ง" : $"Lv.{selectedAccount.MasterLevel} - EXP {selectedAccount.MasterExp:N0} - Quests cleared {selectedAccount.ClearedQuestCount} - Cards owned {selectedAccount.OwnedCardCount:N0}");
	}

	private void RefreshAccountDetails(AccountEntry? account)
	{
		AccountDetailsTable.Items.Clear();
		if (account != null)
		{
			string item = ((account.MasterNextLevelExp > 0) ? $"{account.MasterLevelExp:N0} / {account.MasterNextLevelExp:N0} (เหลืออีก {account.MasterExpToNext:N0})" : "ถึงเลเวลสูงสุดแล้ว");
			(string, string, string)[] array = new(string, string, string)[13]
			{
				("ชื่อมาสเตอร์", account.MasterName, "master"),
				("ID", account.AimeId.ToString(), ""),
				("โหมดบัญชี", account.ModeLabel, ""),
				("เลเวลมาสเตอร์", $"Lv.{account.MasterLevel}", "master"),
				("EXP รวม", $"{account.MasterExp:N0}", ""),
				("EXP เลเวลนี้", item, ""),
				("QP", $"{account.QpAmount:N0}", "qp"),
				("Friend Points", $"{account.FriendPointAmount:N0}", "friend"),
				("Mana Prisms", $"{account.ManaPrismAmount:N0}", "prism"),
				("Summon Points", $"{account.SummonPointAmount:N0}", "summon"),
				("เควสต์ที่เคลียร์", $"{account.ClearedQuestCount} / {account.TrackedQuestCount} ที่ติดตาม", ""),
				("เซอร์แวนต์ที่มี", $"{account.ServantCount:N0}", "master"),
				("Card records", $"{account.SummonHistory.Count:N0} latest acquired / {account.SummonResultCount:N0} printed", "")
			};
			for (int i = 0; i < array.Length; i++)
			{
				(string, string, string) tuple = array[i];
				AccountDetailsTable.Items.Add(AccountDetailRow.Create(tuple.Item1, tuple.Item2));
			}
		}
	}

	private async void RefreshAccountsButton_OnClick(object sender, RoutedEventArgs e)
	{
		await ExecuteAccountActionAsync(() => RefreshAccountsAsync(showErrors: true));
	}

	private async void UseAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (GameBlocksAccountAction())
		{
			return;
		}
		if (await ServerBlocksAccountActionAsync())
		{
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("เลือกบัญชีจากรายการแบบเลื่อนลงด้านบนก่อน", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult result = await RunAccountToolAsync("use", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(result, "use"))
			{
				AppendAccountLog($"Current account set to {account.MasterName} (ID {account.AimeId}); access code written to DEVICE/aime.txt");
				await RefreshAccountsAsync(showErrors: false);
				await RefreshRuntimeStatusAsync();
			}
		});
	}

	private async void CreateAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (GameBlocksAccountAction())
		{
			return;
		}
		if (await ServerBlocksAccountActionAsync())
		{
			return;
		}
		NewAccountDialog newAccountDialog = new NewAccountDialog
		{
			Owner = this
		};
		if (DialogSink.ShowDialog(newAccountDialog) != true)
		{
			return;
		}
		string name = newAccountDialog.AccountName;
		string mode = newAccountDialog.AccountMode;
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("create", "--name", name, "--mode", mode, "--json");
			if (!ReportAccountToolFailure(accountToolResult, "create"))
			{
				int aimeId = (accountToolResult.Root?["aime_id"]?.GetValue<int>()).GetValueOrDefault();
				string accessCode = accountToolResult.Root?["access_code"]?.GetValue<string>() ?? "";
				AppendAccountLog($"Created account {name} (ID {aimeId}), mode {mode}, access code {accessCode}");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(aimeId);
				ThemedMessageBox.Show($"Account created: {name} (ID {aimeId})\nCard ending {accessCode.Substring(Math.Max(0, accessCode.Length - 4))}", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				if (ThemedMessageBox.Show("สลับไปใช้บัญชีใหม่ตอนนี้เลยไหม", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
				{
					AccountToolResult result = await RunAccountToolAsync("use", "--aime-id", aimeId.ToString(), "--json");
					if (!ReportAccountToolFailure(result, "use"))
					{
						AppendAccountLog($"Switched to the new account {name} (ID {aimeId})");
						await RefreshAccountsAsync(showErrors: false);
						ThemedMessageBox.Show($"สลับไปใช้บัญชีใหม่ {name} (ID {aimeId}) แล้ว", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					}
				}
			}
		});
	}

	private async void DeleteAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (GameBlocksAccountAction())
		{
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("เลือกบัญชีที่ต้องการลบจากรายการแบบเลื่อนลงด้านบนก่อน", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (await ServerBlocksAccountActionAsync())
		{
			return;
		}
		string text = (account.IsCurrent ? "\n\nThis is the current account. After it is deleted the launcher switches to a remaining account; if none are left the current card is cleared." : "");
		if (ThemedMessageBox.Show($"Permanently delete the account {account.MasterName} (ID {account.AimeId})?\n\nMaster level: Lv.{account.MasterLevel}\nPrinted cards: {account.OwnedCardCount} unique / {account.OwnedCardCopyCount} copies\nQuests cleared: {account.ClearedQuestCount}\n\n" + "The save data, the account's card, the card mapping and any leftover account backups are deleted permanently, and no recoverable copy is kept." + text, "ยืนยันการลบบัญชี", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("delete", "--aime-id", account.AimeId.ToString(), "--yes", "--json");
			if (!ReportAccountToolFailure(accountToolResult, "delete"))
			{
				bool deletedCurrent = accountToolResult.Root?["deleted_current"]?.GetValue<bool>() == true;
				int replacementAimeId = (accountToolResult.Root?["replacement_aime_id"]?.GetValue<int>()).GetValueOrDefault();
				JsonArray jsonArray = accountToolResult.Root?["backup_cleanup"]?["errors"] as JsonArray;
				bool cleanupComplete = jsonArray == null || jsonArray.Count == 0;
				AppendAccountLog(cleanupComplete ? $"Account {account.MasterName} (ID {account.AimeId}) fully deleted; identity, card mapping and leftover backups cleaned up" : $"Account {account.MasterName} (ID {account.AimeId}) was deleted, but some old backups were not cleaned up");
				if (!cleanupComplete)
				{
					AppendAccountLog("The account was deleted, but some old backups could not be cleaned up: " + string.Join("; ", jsonArray.Select((JsonNode node) => node?.GetValue<string>() ?? "unknown error")));
				}
				await RefreshAccountsAsync(showErrors: false);
				string value = ((!deletedCurrent) ? "" : ((replacementAimeId > 0) ? $"\nบัญชีปัจจุบันถูกสลับไปเป็น ID {replacementAimeId} โดยอัตโนมัติ" : "\nการ์ดปัจจุบันถูกล้างแล้ว - สร้างบัญชีใหม่ก่อนเริ่มเกม"));
				ThemedMessageBox.Show($"ลบบัญชี {account.MasterName} (ID {account.AimeId}) ทั้งหมดเรียบร้อยแล้ว{value}\n\n" + (cleanupComplete ? "ข้อมูลเซฟ การ์ด การจับคู่การ์ด และข้อมูลสำรองที่ค้างอยู่ถูกล้างหมดแล้ว บัญชีใหม่ถัดไปจะใช้ ID ว่างที่ต่ำที่สุด" : "ข้อมูลเซฟ การ์ด และการจับคู่การ์ดถูกลบแล้ว แต่ล้างข้อมูลสำรองเก่าบางส่วนไม่ได้ - ดูได้ในบันทึกทางขวา บัญชีใหม่ถัดไปจะยังคงใช้ ID ว่างที่ต่ำที่สุด"), "FGOAC scooby", MessageBoxButton.OK, cleanupComplete ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
			}
		});
	}

	private async void AccountUpgrade_OnClick(object sender, RoutedEventArgs e)
	{
		if (accountToolRunning || !(sender is Button button))
		{
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			return;
		}
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			ShowGameRunningAccountWarning();
			return;
		}
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			ShowServerRunningAccountWarning();
			return;
		}
		string action = button.Tag.ToString();
		string title = button.Content.ToString();
		// Only Clear Present Box deletes anything, so it alone asks first; every action backs the save up before writing.
		if (action == "clear-gifts" && ThemedMessageBox.Show(this, $"Run {title} on the account {account.MasterName} (ID {account.AimeId})?\n\nThis deletes every unclaimed present without collecting the rewards inside. All other progress is kept, and the save is backed up before anything is written.", title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = (action == "master") ? (await RunAccountScriptAsync(MasterLevelScriptPath, "--aime-id", account.AimeId.ToString(), "--json")) : (await RunAccountToolAsync("upgrade", "--aime-id", account.AimeId.ToString(), "--action", action, "--json"));
			if (!ReportAccountToolFailure(accountToolResult, "upgrade"))
			{
				AppendAccountLog($"{title} finished: {account.MasterName}; backup: {accountToolResult.Root?["backup"]}");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(account.AimeId);
				ThemedMessageBox.Show(this, $"บัญชี {account.MasterName}: {title} เสร็จสิ้น", title);
			}
		});
	}

	private async void ResetAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (GameBlocksAccountAction())
		{
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("เลือกบัญชีที่ต้องการรีเซ็ตจากรายการแบบเลื่อนลงด้านบนก่อน", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (await ServerBlocksAccountActionAsync())
		{
			return;
		}
		if (ThemedMessageBox.Show($"รีเซ็ตบัญชี {account.MasterName} (ID {account.AimeId}) ให้เป็นบัญชีปกติที่สร้างใหม่หรือไม่\n\nการทำเช่นนี้จะล้างเซอร์แวนต์ที่มี แต้ม ไอเทม ประวัติการพิมพ์ และความคืบหน้าในเกมทั้งหมด การรีเซ็ตบัญชีปัจจุบันจะล้างเด็คออกรบด้วย และย้อนกลับไม่ได้", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult result = await RunAccountToolAsync("reset", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(result, "reset"))
			{
				if (account.IsCurrent)
				{
					cardCollection.DeselectAll();
					selectedCardListView.Refresh();
					cardListView.Refresh();
					PublishDeck();
				}
				AppendAccountLog($"Account {account.MasterName} (ID {account.AimeId}) reset to a new normal account");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(account.AimeId);
				ThemedMessageBox.Show("รีเซ็ตเสร็จสิ้น: บัญชี " + account.MasterName + " ถูกล้างเซอร์แวนต์ ทรัพยากร และความคืบหน้าแล้ว", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}

	private async void RepairAccountButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (GameBlocksAccountAction())
		{
			return;
		}
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("เลือกบัญชีที่ต้องการซ่อมแซมก่อน", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		if (await ServerBlocksAccountActionAsync())
		{
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("repair", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(accountToolResult, "repair"))
			{
				int captures = (accountToolResult.Root?["repair"]?["captures"]?.GetValue<int>()).GetValueOrDefault();
				int questRows = (accountToolResult.Root?["repair"]?["quest_rows"]?.GetValue<int>()).GetValueOrDefault();
				AppendAccountLog($"Account {account.AimeId} repaired from history: {captures} results, {questRows} quest states");
				await RefreshAccountsAsync(showErrors: false);
				SelectAccountById(account.AimeId);
				ThemedMessageBox.Show($"ซ่อมแซมเสร็จสิ้น: ประมวลผลผลลัพธ์ย้อนหลัง {captures} รายการ และสถานะเควสต์ {questRows} รายการ", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		});
	}
}
