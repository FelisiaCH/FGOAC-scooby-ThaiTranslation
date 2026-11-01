using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private sealed class BenefitGrantEntry
	{
		public string Key { get; init; } = "";

		public string Category { get; init; } = "";

		public string Name { get; init; } = "";

		public string ItemId { get; init; } = "";

		public int Current { get; init; }

		public int MaxAmount { get; init; }

		public int GrantAmount { get; set; }

		public string GrantText { get; set; } = "0";
	}

	private async void GrantBenefitsButton_OnClick(object sender, RoutedEventArgs e)
	{
		AccountEntry account = SelectedAccount;
		if (account == null)
		{
			ThemedMessageBox.Show("เลือกบัญชีที่จะรับไอเทมโบนัสก่อน", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		await ExecuteAccountActionAsync(async delegate
		{
			AccountToolResult accountToolResult = await RunAccountToolAsync("gift", "--aime-id", account.AimeId.ToString(), "--json");
			if (!ReportAccountToolFailure(accountToolResult, "catalog"))
			{
				List<BenefitGrantEntry> entries = new List<BenefitGrantEntry>();
				if (accountToolResult.Root?["items"] is JsonArray jsonArray)
				{
					foreach (JsonNode item in jsonArray)
					{
						if (item is JsonObject jsonObject)
						{
							entries.Add(new BenefitGrantEntry
							{
								Key = (jsonObject["key"]?.GetValue<string>() ?? ""),
								Category = (jsonObject["category"]?.GetValue<string>() ?? ""),
								Name = (jsonObject["name"]?.GetValue<string>() ?? ""),
								ItemId = (jsonObject["item_id"]?.ToString() ?? ""),
								Current = (jsonObject["current"]?.GetValue<int>() ?? 0),
								MaxAmount = (jsonObject["max_amount"]?.GetValue<int>() ?? 0)
							});
						}
					}
				}
				if (entries.Count == 0)
				{
					ThemedMessageBox.Show("ข้อมูลที่ติดตั้งไว้ไม่มีไอเทมโบนัสที่มอบให้ได้", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				else
				{
					DataGrid table = new DataGrid
					{
						AutoGenerateColumns = false,
						CanUserAddRows = false,
						CanUserDeleteRows = false,
						HeadersVisibility = DataGridHeadersVisibility.Column,
						GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
						ItemsSource = entries,
						Margin = new Thickness(10.0),
						SelectionMode = DataGridSelectionMode.Single
					};
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "หมวดหมู่",
						Binding = new Binding("หมวดหมู่"),
						Width = 120.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "ไอเทม",
						Binding = new Binding("Name"),
						Width = new DataGridLength(1.0, DataGridLengthUnitType.Star),
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "ID",
						Binding = new Binding("ItemId"),
						Width = 105.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "ปัจจุบัน",
						Binding = new Binding("ปัจจุบัน")
						{
							StringFormat = "N0"
						},
						Width = 100.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "สูงสุด",
						Binding = new Binding("MaxAmount")
						{
							StringFormat = "N0"
						},
						Width = 110.0,
						IsReadOnly = true
					});
					table.Columns.Add(new DataGridTextColumn
					{
						Header = "จำนวนที่จะมอบ",
						Binding = new Binding("GrantText")
						{
							Mode = BindingMode.TwoWay,
							UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
						},
						Width = 145.0,
						EditingElementStyle = BenefitAmountEditorStyle()
					});
					Button button = new Button
					{
						Content = "ส่งไปยังกล่องของขวัญ",
						MinWidth = 165.0,
						Margin = new Thickness(5.0)
					};
					Button element = new Button
					{
						Content = "ยกเลิก",
						MinWidth = 90.0,
						Margin = new Thickness(5.0),
						IsCancel = true
					};
					StackPanel stackPanel = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						HorizontalAlignment = HorizontalAlignment.Right,
						Margin = new Thickness(10.0, 0.0, 10.0, 10.0)
					};
					stackPanel.Children.Add(element);
					stackPanel.Children.Add(button);
					Button button2 = new Button
					{
						Content = "เติมทั้งหมดจนเต็ม",
						Margin = new Thickness(5.0)
					};
					Button button3 = new Button
					{
						Content = "ล้างทั้งหมด",
						Margin = new Thickness(5.0)
					};
					stackPanel.Children.Insert(0, button2);
					stackPanel.Children.Insert(1, button3);
					button2.Click += delegate
					{
						table.CancelEdit();
						table.CancelEdit(DataGridEditingUnit.Row);
						foreach (BenefitGrantEntry item2 in entries)
						{
							item2.GrantText = Math.Max(0, item2.MaxAmount - item2.Current).ToString();
						}
						table.Items.Refresh();
					};
					button3.Click += delegate
					{
						table.CancelEdit();
						table.CancelEdit(DataGridEditingUnit.Row);
						foreach (BenefitGrantEntry item3 in entries)
						{
							item3.GrantText = "0";
						}
						table.Items.Refresh();
					};
					DockPanel dockPanel = new DockPanel();
					TextBlock element2 = new TextBlock
					{
						Text = "ใส่จำนวน หรือใช้เติมทั้งหมดจนเต็ม ค่า 0 คือไม่ส่งอะไรเลย เซิร์ฟเวอร์จะวางไอเทมไว้ในกล่องของขวัญเมื่อเกมร้องขอครั้งถัดไป และไอเทมจะเข้าคลังของคุณเมื่อคุณกดรับ จำนวนจะถูกจำกัดไม่เกินค่าสูงสุด โดยนับรวมสิ่งที่คุณถืออยู่แล้วและสิ่งที่ยังรออยู่ในกล่องของขวัญ รายการนี้แสดงเฉพาะไอเทมที่มีโปรโตคอลการมอบซึ่งยืนยันแล้วเท่านั้น",
						Margin = new Thickness(12.0, 10.0, 12.0, 0.0),
						Foreground = Brushes.LightGray,
						TextWrapping = TextWrapping.Wrap
					};
					DockPanel.SetDock(element2, Dock.Top);
					DockPanel.SetDock(stackPanel, Dock.Bottom);
					dockPanel.Children.Add(element2);
					dockPanel.Children.Add(stackPanel);
					dockPanel.Children.Add(table);
					Window dialog = new Window
					{
						Owner = this,
						Title = $"Grant Bonus Items - {account.MasterName} (ID {account.AimeId})",
						Width = 980.0,
						Height = 680.0,
						MinWidth = 760.0,
						MinHeight = 460.0,
						WindowStartupLocation = WindowStartupLocation.CenterOwner,
						Background = new SolidColorBrush(Color.FromRgb(12, 10, 14)),
						Foreground = Brushes.White,
						Content = dockPanel
					};
					WindowTheme.Apply(dialog);
					button.Click += delegate
					{
						table.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
						table.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
						foreach (BenefitGrantEntry item4 in entries)
						{
							if (!int.TryParse(item4.GrantText, NumberStyles.None, CultureInfo.InvariantCulture, out var result))
							{
								ThemedMessageBox.Show("จำนวนต้องเป็นจำนวนเต็มตั้งแต่ 0 ถึง 2,147,483,647", "ไอเทมโบนัส");
								return;
							}
							item4.GrantAmount = result;
						}
						if (!entries.Any((BenefitGrantEntry item) => item.GrantAmount > 0))
						{
							ThemedMessageBox.Show("ใส่จำนวนมากกว่า 0 ให้อย่างน้อยหนึ่งไอเทม", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
						}
						else
						{
							dialog.DialogResult = true;
						}
					};
					if (DialogSink.ShowDialog(dialog) == true)
					{
						List<string> list = new List<string>
						{
							"gift",
							"--aime-id",
							account.AimeId.ToString()
						};
						foreach (BenefitGrantEntry item5 in entries.Where((BenefitGrantEntry item) => item.GrantAmount > 0))
						{
							list.Add("--item");
							list.Add($"{item5.Key}={item5.GrantAmount}");
						}
						list.Add("--json");
						AccountToolResult accountToolResult2 = await RunAccountToolAsync(list.ToArray());
						if (!ReportAccountToolFailure(accountToolResult2, "grant"))
						{
							(string status, string line) = GrantResult(accountToolResult2.Root, account.MasterName, account.AimeId);
							AppendAccountLog(line);
							await RefreshAccountsAsync(showErrors: false);
							SelectAccountById(account.AimeId);
							AccountStatusText.Text = status;
							AccountStatusText.Visibility = Visibility.Visible;
							HoldStatus(status);
						}
					}
				}
			}
		});
	}

	/// <summary>The Status sentence and the Game log line for a grant the account tool queued, from its "grants" list.</summary>
	private static (string Status, string Line) GrantResult(JsonObject? result, string masterName, int aimeId)
	{
		List<string> items = new List<string>();
		if (result?["grants"] is JsonArray grants)
		{
			foreach (JsonNode? grant in grants)
			{
				if (grant is JsonObject item)
				{
					items.Add($"{item["name"]?.GetValue<string>() ?? "Item"} {(item["applied"]?.GetValue<int>() ?? 0):N0}");
				}
			}
		}
		string what = (items.Count == 1) ? "1 bonus item" : $"{items.Count} bonus items";
		return ($"Sent {what} to the Present Box of {masterName}. Open the Present Box in the game to claim {((items.Count == 1) ? "it" : "them")}.", $"Granted {what} to account {aimeId}: " + string.Join(", ", items));
	}

	private static Style BenefitAmountEditorStyle()
	{
		return new Style(typeof(TextBox))
		{
			Setters = 
			{
				(SetterBase)new Setter(Control.ForegroundProperty, Brushes.White),
				(SetterBase)new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(24, 19, 27))),
				(SetterBase)new Setter(TextBoxBase.CaretBrushProperty, Brushes.White),
				(SetterBase)new Setter(TextBoxBase.SelectionBrushProperty, Brushes.DarkMagenta),
				(SetterBase)new Setter(Control.PaddingProperty, new Thickness(4.0, 2.0, 4.0, 2.0)),
				(SetterBase)new Setter(FrameworkElement.MarginProperty, new Thickness(0.0))
			}
		};
	}
}
