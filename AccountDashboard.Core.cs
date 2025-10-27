#region using
using Newtonsoft.Json;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

#endregion

namespace NinjaTrader.AddOns
{
    public partial class AccountDashboard : AddOnBase
    {
        // Data
        internal ObservableCollection<AccountRow> Rows;
        internal ObservableCollection<SummaryRow> Summaries;
        internal ICollectionView View;

        // Timer for batching UI refresh
        DispatcherTimer refreshTimer;
		DispatcherTimer connectionTimer;

		HashSet<string> prevAccounts = new( StringComparer.OrdinalIgnoreCase );
		HashSet<string> addedAccounts = new( StringComparer.OrdinalIgnoreCase );


		// Paths
		string SettingsPath;
        string LogPath;

		// Config object for JSON
		class PersistModel
		{
			public class RowCfg
			{
				public string Account;
				public int Role;
				public bool Hidden;
				public bool RiskEnabled;
				public int Size;
				public double DailyGoal;
				public double DailyLoss;
				public double AutoLiq;
				public double StartBalance;
				public double DayRealizedStart;
			}

			public class ColumnCfg
			{
				public string Header;
				public double Width;
				public int DisplayIndex;
				public bool Visible;
				public bool IsSorted;
				public ListSortDirection? SortDirection;
			}

			public RowCfg[] Rows;
			public List<ColumnCfg> Columns;

			public double WindowLeft;
			public double WindowTop;
			public double WindowWidth;
			public double WindowHeight;

			public bool ShowSim;
			public bool CopierEnabled;
			public bool GroupByConnectionEnabled;
		}

		protected override void OnStateChange()
		{
			//Print( State );
			if(State == State.SetDefaults)
			{
				Name = "Account Dashboard";
			}
			else if(State == State.Configure)
			{
				SettingsPath = Path.Combine(
					Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ),
					"NinjaTrader 8", "templates", "AccountDashboard", "RAGEAccountDashboardSettings.json" );

				LogPath = Path.Combine(
					Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ),
					"NinjaTrader 8", "log", "RAGEAccountDashboard.log" );

				Directory.CreateDirectory( Path.GetDirectoryName( SettingsPath ) );
				Directory.CreateDirectory( Path.GetDirectoryName( LogPath ) );
				ADLog.LogFilePath = LogPath;

				Rows = new ObservableCollection<AccountRow>();
				Summaries = new ObservableCollection<SummaryRow>();

				/*lock(Account.All)
				{
					foreach(var a in Account.All)
					{
						if(a.ConnectionStatus != ConnectionStatus.Connected)
							continue;

						//if( a.GetAccountItem( AccountItem.TrailingMaxDrawdown, Currency.UsDollar ).Value < 0 )
						//continue;

						var row = new AccountRow( a, canChangeRole: _ => true, onRoleClick: OnRoleButtonClicked );
						Rows.Add( row );

						//Print( $"Assign events to Account {a.Name}" );
						a.AccountItemUpdate += OnAccountItemUpdate;
						a.ExecutionUpdate += OnExecutionUpdate;
						a.OrderUpdate += OnOrderUpdate;

						// First call
						Print( $"Assign values to Account {a.Name}" );
						SafeGet( a, AccountItem.UnrealizedProfitLoss );
						SafeGet( a, AccountItem.CashValue );
						SafeGet( a, AccountItem.RealizedProfitLoss );
						SafeGet( a, AccountItem.UnrealizedProfitLoss );
						SafeGet( a, AccountItem.Commission );
						SafeGet( a, AccountItem.TrailingMaxDrawdown );

						row.StartBalance = SafeGet( a, AccountItem.CashValue );
						row.DayRealizedStart = SafeGet( a, AccountItem.RealizedProfitLoss );
					}
				}

				View = System.Windows.Data.CollectionViewSource.GetDefaultView( Rows );
				View.Filter = o => o is AccountRow r && !r.Hidden;

				//LoadSettings();

				refreshTimer = new DispatcherTimer( DispatcherPriority.Background )
				{ Interval = TimeSpan.FromMilliseconds( 750 ) };
				refreshTimer.Tick += ( s, e ) => RecalcSummaries();
				refreshTimer.Start();*/
			}
			else if(State == State.Terminated)
			{
				CopierMgr.ClearAll();
				/*lock(Account.All)
				{
					foreach(var a in Account.All)
					{
						//if(a.ConnectionStatus != ConnectionStatus.Connected)
						//continue;

						a.AccountItemUpdate -= OnAccountItemUpdate;
						a.ExecutionUpdate -= OnExecutionUpdate;
						a.OrderUpdate -= OnOrderUpdate;
					}
				}*/
			}
		}


		void SafeApplyGrouping()
		{
			try
			{
				if(MainGrid == null)
				{
					Print( "MainGrid is null" );
					return;
				}
				if(MainGrid.ItemsSource == null)
				{
					Print( "MainGrid ItemsSource is null" );
					return;
				}

				var uiDispatcher = MainGrid.Dispatcher;
				if(!uiDispatcher.CheckAccess())
				{
					uiDispatcher.InvokeAsync( SafeApplyGrouping, DispatcherPriority.ContextIdle );
					return;
				}

				// --- run on UI thread ---
				View.GroupDescriptions.Clear();
				MainGrid.GroupStyle.Clear();

				if(groupByConnectionEnabled)
				{
					View.GroupDescriptions.Add( new PropertyGroupDescription( nameof( AccountRow.ConnectionName ) ) );
					MainGrid.GroupStyle.Add( BuildConnectionHeaderGroupStyle() );
				}

				View.Refresh();
			}
			catch(Exception ex)
			{
				Print( "SafeApplyGrouping error: " + ex.Message );
			}
		}

		// assume you have groupToggle declared at UI scope
		void ApplyGroupingSafe()
		{
			try
			{
				//Print( "ApplyGroupingSafe" );
				// ensure UI thread and apply after current layout pass
				//Application.Current?.Dispatcher?.InvokeAsync( () =>
				//{
				if(MainGrid == null || MainGrid.ItemsSource == null)
				{
					Print( "MainGrid is null" );
					return;
				}

				if(groupByConnectionEnabled)
				{
					View.GroupDescriptions.Clear();
					View.GroupDescriptions.Add( new PropertyGroupDescription( nameof( AccountRow.ConnectionName ) ) );

					//var VisualTree = new FrameworkElementFactory( typeof( TextBlock ), "header" );
					//VisualTree.SetBinding( TextBlock.TextProperty, new Binding( "Name" ) );
					//VisualTree.SetValue( TextBlock.FontWeightProperty, FontWeights.Bold );
					//VisualTree.SetValue( TextBlock.FontSizeProperty, 12.0 );
					//VisualTree.SetValue( TextBlock.ForegroundProperty, Brushes.White );
					//VisualTree.SetValue( TextBlock.PaddingProperty, new Thickness( 0 ) );
					//VisualTree.SetValue( TextBlock.MarginProperty, new Thickness( 0, 4, 0, 0 ) );
					////VisualTree.SetValue( TextBlock.MarginProperty, new Thickness( 0 ) );
					////VisualTree.SetValue( TextBlock.MarginProperty, new Thickness( 4, 8, 0, 2 ) );

					MainGrid.GroupStyle.Clear();

					var gs = BuildConnectionHeaderGroupStyle();
					MainGrid.GroupStyle.Add( gs );
					/*MainGrid.GroupStyle.Add( new GroupStyle
					{
						HeaderTemplate = new DataTemplate
						{
							VisualTree = VisualTree
						},
						// optional: add padding or hide group borders
						ContainerStyle = new Style( typeof( GroupItem ) )
						{
							Setters = { new Setter( Control.MarginProperty, new Thickness( 0 ) ), new Setter( Control.PaddingProperty, new Thickness( 0 ) ) }
						}
					} );*/
					View.Refresh();
				}
				else
				{
					View.GroupDescriptions.Clear();
					MainGrid.GroupStyle.Clear();
					View.Refresh();
				}

				/*Print( "ApplyGroupingSafe 2" );
				if(MainGrid == null || MainGrid.ItemsSource == null) return;

				Print( "ApplyGroupingSafe 3" );
				var view = View; // CollectionViewSource.GetDefaultView( MainGrid.ItemsSource );
				if(view == null) return;

				Print( "ApplyGroupingSafe 4" );
				if(view.GroupDescriptions != null ) 
					view.GroupDescriptions.Clear();
				//if(MainGrid.GroupStyle != null)
					//MainGrid.GroupStyle.Clear();

				Print( "ApplyGroupingSafe 5" );
				if(groupByConnectionEnabled)
				{
					Print( "ApplyGroupingSafe 6" );
					view.GroupDescriptions.Add( new PropertyGroupDescription( nameof( AccountRow.ConnectionName ) ) );
					MainGrid.GroupStyle.Add( BuildConnectionHeaderGroupStyle() );
				}

				Print( "ApplyGroupingSafe 7" );
				view.Refresh();*/
				//}, System.Windows.Threading.DispatcherPriority.ContextIdle );
			}
			catch(Exception ex)
			{
				Print( "ApplyGroupingSafe: "  + ex.Message );
			}
		}

		GroupStyle BuildConnectionHeaderGroupStyle()
		{
			var gs = new GroupStyle();

			// NOTE: To remove a slight indent applied by the grouping we add 6 margin to title and -6 to the rows (assuming it's half the font size.. not sure but it looks good like that)
			// If things change this might need some adjustments

			// build a simple header template
			var f = new FrameworkElementFactory( typeof( TextBlock ) );
			f.SetBinding( TextBlock.TextProperty, new Binding( "Name" ) );
			f.SetValue( TextBlock.FontWeightProperty, FontWeights.SemiBold );
			f.SetValue( TextBlock.FontSizeProperty, 12.0 );
			f.SetValue( TextBlock.ForegroundProperty, ADTheme.Fore );
			f.SetValue( TextBlock.PaddingProperty, new Thickness( 0 ) );
			f.SetValue( TextBlock.MarginProperty, new Thickness( 6, 4, 0, 0 ) );
			gs.HeaderTemplate = new DataTemplate { VisualTree = f };

			// Remove indent
			var style = new Style( typeof( GroupItem ) );
			style.Setters.Add( new Setter( Control.MarginProperty, new Thickness( -6, 0, 0, 0 ) ) );
			style.Setters.Add( new Setter( Control.PaddingProperty, new Thickness( 0 ) ) );

			// Critical: flatten panel, so no nested indent
			gs.Panel = new ItemsPanelTemplate(
				new FrameworkElementFactory( typeof( StackPanel ) )
			);
			gs.ContainerStyle = style;
			return gs;
		}


		internal void InitializeAccountsDiff()
		{
			try
			{
				// cache roles before reset
				var savedRoles = Rows?.ToDictionary( r => r.AccountName, r => new { r.Role, r.Size } ) ?? new();

				var oldRows = Rows.ToDictionary( r => r.AccountName );
				var liveAccounts = Account.All
					.Where( a => a.ConnectionStatus == ConnectionStatus.Connecting || a.ConnectionStatus == ConnectionStatus.Connected )
					.ToDictionary( a => a.Name, a => a );

				// Remove old
				foreach(var dead in oldRows.Keys.Except( liveAccounts.Keys ).ToList())
				{
					var row = oldRows[ dead ];
					row.Acct.AccountItemUpdate -= OnAccountItemUpdate;
					row.Acct.PositionUpdate -= OnPositionUpdate;
					row.Acct.ExecutionUpdate -= OnExecutionUpdate;
					Rows.Remove( row );

					//Print( "Remove dead row: " + row.AccountName );
				}

				// Add new
				foreach(var add in liveAccounts.Keys.Except( oldRows.Keys ))
				{
					var acct = liveAccounts[ add ];
					acct.AccountItemUpdate += OnAccountItemUpdate;
					acct.PositionUpdate += OnPositionUpdate;
					acct.ExecutionUpdate += OnExecutionUpdate;

					var newRow = new AccountRow( acct, _ => true, OnRoleButtonClicked );
					
					//Print( "Add new row: " + newRow.AccountName );

					newRow.CashValue = SafeGet( acct, AccountItem.CashValue );
					newRow.Realized = SafeGet( acct, AccountItem.RealizedProfitLoss );
					newRow.Unrealized = SafeGet( acct, AccountItem.UnrealizedProfitLoss );
					newRow.Commissions = SafeGet( acct, AccountItem.Commission );
					newRow.TrailingMaxDrawdown = SafeGet( acct, AccountItem.TrailingMaxDrawdown );
					newRow.StartBalance = SafeGet( acct, AccountItem.CashValue );
					newRow.DayRealizedStart = SafeGet( acct, AccountItem.RealizedProfitLoss );

					// --- existing position at startup ---
					if(acct.Positions != null && acct.Positions.Count > 0)
					{
						double totalUPNL = acct.Positions.Sum( p => p.GetUnrealizedProfitLoss( PerformanceUnit.Currency ) );
						int totalQty = acct.Positions.Sum( p => Math.Abs( p.Quantity ) );
						var hasLong = acct.Positions.Any( p => p.MarketPosition == MarketPosition.Long );
						var hasShort = acct.Positions.Any( p => p.MarketPosition == MarketPosition.Short );

						newRow.Pos = totalQty;
						newRow.Qty = totalQty;
						newRow.Dir = hasLong && hasShort ? (MarketPosition)(-999) :
								  hasLong ? MarketPosition.Long :
								  hasShort ? MarketPosition.Short :
								  MarketPosition.Flat;
					}
					else
					{
						newRow.Dir = MarketPosition.Flat;
						newRow.Pos = 0;
						newRow.Qty = 0;
					}

					// restore previous role and size if available
					if(savedRoles.TryGetValue( acct.Name, out var saved ))
					{
						newRow.Role = saved.Role;
						newRow.Size = saved.Size;
						if(newRow.Role == RowRole.Master)
						{
							CopierMgr.SetMaster( newRow.Acct );
						}
						else if(newRow.Role == RowRole.Follower)
						{
							CopierMgr.AddFollower( newRow.Acct );
						}
					}

					Rows.Add( newRow );
				}

				// Filters
				View = System.Windows.Data.CollectionViewSource.GetDefaultView( Rows );
				View.Filter = o =>
				{
					if(o is not AccountRow r) return false;
					if(r.Hidden) return false;
					if(!showSim && r.AccountName.StartsWith( "Sim", StringComparison.OrdinalIgnoreCase ))
						return false;
					return true;
				};
				//View.Filter = o => o is AccountRow r && !r.Hidden;

				// Run once and then use the timer
				if(refreshTimer != null)
				{
					refreshTimer.Stop();
					refreshTimer = null;
				}
				refreshTimer = new DispatcherTimer( DispatcherPriority.Background )
				{ Interval = TimeSpan.FromMilliseconds( 333 ) };
				refreshTimer.Tick += ( s, e ) => RecalcSummaries();
				refreshTimer.Start();

				// --- connection monitor ---
				if(connectionTimer != null)
				{
					connectionTimer.Stop();
					connectionTimer = null;
				}
				connectionTimer = new DispatcherTimer( DispatcherPriority.Background )
				{
					//Interval = TimeSpan.FromMilliseconds( 666 )
					Interval = TimeSpan.FromSeconds( 2 )
				};
				connectionTimer.Tick += ( s, e ) => CheckConnections();
				connectionTimer.Start();

				// Existing ones need no resubscription or recreation
				SafeApplyGrouping();
				RecalcSummaries();
				View.Refresh();
			}
			catch(Exception ex)
			{
				Print( "InitializeAccountsDiff: " + ex.Message );
			}
		}

		internal void InitializeAccounts()
		{
			// cache roles before reset
			var savedRoles = Rows?.ToDictionary( r => r.AccountName, r => new { r.Role, r.Size } )
							 ?? new();

			/*if(Rows == null)
				Rows = new ObservableCollection<AccountRow>();
			if(Summaries == null)
				Summaries = new ObservableCollection<SummaryRow>();*/

			//CopierMgr.ClearAll();

			Rows.Clear();

			foreach(var acct in Account.All)
			{
				if(acct.ConnectionStatus != ConnectionStatus.Connected)
					continue;

				acct.PositionUpdate += OnPositionUpdate;
				acct.AccountItemUpdate += OnAccountItemUpdate;
				acct.ExecutionUpdate += OnExecutionUpdate;

				//Print( $"Add new row {a.Name}");
				var row = new AccountRow( acct, canChangeRole: _ => true, onRoleClick: OnRoleButtonClicked );

				// restore previous role and size if available
				if(savedRoles.TryGetValue( acct.Name, out var saved ))
				{
					row.Role = saved.Role;
					row.Size = saved.Size;
					if(row.Role == RowRole.Master)
					{
						CopierMgr.SetMaster( row.Acct );
					}
					else if(row.Role == RowRole.Follower)
					{
						CopierMgr.AddFollower( row.Acct );
					}
				}

				Rows.Add( row );

				//Print( $"Assign values to Account {a.Name}" );
				row.CashValue = SafeGet( acct, AccountItem.CashValue );
				row.Realized = SafeGet( acct, AccountItem.RealizedProfitLoss );
				row.Unrealized = SafeGet( acct, AccountItem.UnrealizedProfitLoss );
				row.Commissions = SafeGet( acct, AccountItem.Commission );
				row.TrailingMaxDrawdown = SafeGet( acct, AccountItem.TrailingMaxDrawdown );
				row.StartBalance = SafeGet( acct, AccountItem.CashValue );
				row.DayRealizedStart = SafeGet( acct, AccountItem.RealizedProfitLoss );

				// --- existing position at startup ---
				if(acct.Positions != null && acct.Positions.Count > 0)
				{
					double totalUPNL = acct.Positions.Sum( p => p.GetUnrealizedProfitLoss( PerformanceUnit.Currency ) );
					int totalQty = acct.Positions.Sum( p => Math.Abs( p.Quantity ) );
					var hasLong = acct.Positions.Any( p => p.MarketPosition == MarketPosition.Long );
					var hasShort = acct.Positions.Any( p => p.MarketPosition == MarketPosition.Short );

					row.Pos = totalQty;
					row.Qty = totalQty;
					row.Dir = hasLong && hasShort ? (MarketPosition)(-999) :
							  hasLong ? MarketPosition.Long :
							  hasShort ? MarketPosition.Short :
							  MarketPosition.Flat;
				}
				else
				{
					row.Dir = MarketPosition.Flat;
					row.Pos = 0;
					row.Qty = 0;
				}
				/*var pos = a.Positions?.FirstOrDefault();
				if(pos != null)
				{
					row.Dir = pos.MarketPosition;
					row.Pos = Math.Abs( pos.Quantity );
					row.Qty = Math.Abs( pos.Quantity );
				}
				else
				{
					row.Dir = MarketPosition.Flat;
					row.Pos = 0;
					row.Qty = 0;
				}*/
			}

			// Filters
			View = System.Windows.Data.CollectionViewSource.GetDefaultView( Rows );
			View.Filter = o =>
			{
				if(o is not AccountRow r) return false;
				if(r.Hidden) return false;
				if(!showSim && r.AccountName.StartsWith( "Sim", StringComparison.OrdinalIgnoreCase ))
					return false;
				return true;
			};
			//View.Filter = o => o is AccountRow r && !r.Hidden;

			// Run once and then use the timer
			RecalcSummaries();
			if(refreshTimer != null)
			{
				refreshTimer.Stop();
				refreshTimer = null;
			}
			if(refreshTimer == null)
			{
				refreshTimer = new DispatcherTimer( DispatcherPriority.Background )
				{ Interval = TimeSpan.FromMilliseconds( 333 ) };
				refreshTimer.Tick += ( s, e ) => RecalcSummaries();
				refreshTimer.Start();
			}
			// --- connection monitor ---
			if(connectionTimer != null)
			{
				connectionTimer.Stop();
				connectionTimer = null;
			}
			if(connectionTimer == null)
			{
				connectionTimer = new DispatcherTimer( DispatcherPriority.Background )
				{
					Interval = TimeSpan.FromSeconds( 2 )
				};
				connectionTimer.Tick += ( s, e ) => CheckConnections();
				connectionTimer.Start();
			}

			SafeApplyGrouping();
			//Application.Current?.Dispatcher.InvokeAsync( () => ApplyGroupingSafe(), DispatcherPriority.ContextIdle );
			//ApplyGroupingSafe();
		}


		internal void UninitializeAccounts()
		{
			CopierMgr.ClearAll();

			if(refreshTimer != null)
			{
				refreshTimer.Stop();
				refreshTimer = null;
			}

			if(connectionTimer != null)
			{
				connectionTimer.Stop();
				connectionTimer = null;
			}

			foreach(var r in Rows)
			{
				r.Acct.PositionUpdate -= OnPositionUpdate;
				r.Acct.AccountItemUpdate -= OnAccountItemUpdate;
				r.Acct.ExecutionUpdate -= OnExecutionUpdate;
			}
		}


		Dictionary<string, DateTime> disconnectTimes = new( StringComparer.OrdinalIgnoreCase );
		const int DisconnectGraceSeconds = 15;


		void CheckConnections()
		{
			try
			{
				// refresh connection indicators on all rows
				foreach(var r in Rows)
				{
					if(r.Acct == null) continue;
					r.OnChanged( nameof( r.ConnStatus ) );
				}

				var now = DateTime.UtcNow;

				// current live connected account names
				var connected = Account.All
					.Where( a => a.ConnectionStatus == ConnectionStatus.Connecting || a.ConnectionStatus == ConnectionStatus.Connected )
					.Select( a => a.Name )
					.ToHashSet( StringComparer.OrdinalIgnoreCase );

				addedAccounts = connected.Except( prevAccounts ).ToHashSet( StringComparer.OrdinalIgnoreCase );
				var added = connected.Except( prevAccounts ).ToList();
				var removed = prevAccounts.Except( connected ).ToList();

				// nothing changed and no pending disconnects
				if(added.Count == 0 && removed.Count == 0 && disconnectTimes.Count == 0)
					return;

				//-----------------------------------------------------
				// Handle removed (disconnected) accounts
				//-----------------------------------------------------
				if(removed.Count > 0)
				{
					foreach(var row in Rows)
					{
						if(!connected.Contains( row.AccountName ))
						{
							// mark disconnect time once
							if(!disconnectTimes.ContainsKey( row.AccountName ))
							{
								disconnectTimes[ row.AccountName ] = now;
								PulseRow( row, ADTheme.DisconnectRow );
							}
						}
						else
						{
							// came back online
							PulseRow( row, ADTheme.ConnectRow );
							disconnectTimes.Remove( row.AccountName );
						}
					}
				}
				else
				{
					// mark reconnections for already tracked rows
					foreach(var row in Rows)
					{
						if(connected.Contains( row.AccountName ))
							disconnectTimes.Remove( row.AccountName );
					}
				}

				//-----------------------------------------------------
				// Remove expired disconnected accounts
				//-----------------------------------------------------
				var expired = disconnectTimes
					.Where( kvp => (now - kvp.Value).TotalSeconds > DisconnectGraceSeconds )
					.Select( kvp => kvp.Key )
					.ToList();

				if(expired.Count > 0)
				{
					ADLog.Write( $"Removing {expired.Count} accounts after timeout" );

					MainGrid?.Dispatcher.Invoke( () =>
					{
						foreach(var name in expired)
						{
							var row = Rows.FirstOrDefault( r => r.AccountName.Equals( name, StringComparison.OrdinalIgnoreCase ) );
							if(row != null)
							{
								row.Acct.AccountItemUpdate -= OnAccountItemUpdate;
								row.Acct.PositionUpdate -= OnPositionUpdate;
								row.Acct.ExecutionUpdate -= OnExecutionUpdate;
								Rows.Remove( row );
							}
							disconnectTimes.Remove( name );
						}
						RecalcSummaries();
						View.Refresh();
					} );
				}

				//-----------------------------------------------------
				// Handle new connections or replacements
				//-----------------------------------------------------
				if(added.Count > 0 || expired.Count > 0)
				{
					InitializeAccountsDiff();
					MainGrid?.Dispatcher.InvokeAsync( () =>
					{
						foreach(var r in Rows.Where( x => added.Contains( x.AccountName ) ))
							PulseRow( r, ADTheme.ConnectRow );
					}, DispatcherPriority.Render );
				}

				prevAccounts = connected;
				View.Refresh();

				if(CopierMgr != null )
					CopierMgr.RefreshAccountsAfterReconnect();
			}
			catch(Exception ex)
			{
				ADLog.Write( $"CheckConnections error: {ex.Message}" );
			}
		}


		void CheckConnections222()
		{
			try
			{
				//Print( "Check connections" );

				foreach(var r in Rows)
				{
					if(r.Acct == null) continue;
					//if(r.Hidden) continue;
					r.OnChanged( nameof( r.ConnStatus ) );
				}

				var now = DateTime.UtcNow;

				var connected = Account.All
					.Where( a => a.ConnectionStatus == ConnectionStatus.Connecting || a.ConnectionStatus == ConnectionStatus.Connected )
					.Select( a => a.Name )
					.ToHashSet( StringComparer.OrdinalIgnoreCase );

				addedAccounts = connected.Except( prevAccounts ).ToHashSet( StringComparer.OrdinalIgnoreCase );
				var added = connected.Except( prevAccounts ).ToList();
				var removed = prevAccounts.Except( connected ).ToList();

				// nothing changed
				if(added.Count == 0 && removed.Count == 0)
					return;

				if(removed.Count > 0)
				{
					// Mark disconnect times
					foreach(var row in Rows)
					{
						if(!connected.Contains( row.AccountName ))
						{
							if(!disconnectTimes.ContainsKey( row.AccountName ))
							{
								disconnectTimes[ row.AccountName ] = now;  // first detected disconnect
								PulseRow( row, ADTheme.DisconnectRow );
							}
						}
						else
						{
							// back online
							PulseRow( row, ADTheme.ConnectRow );
							disconnectTimes.Remove( row.AccountName );
						}
					}

					// Remove accounts that exceeded grace period
					var expired = disconnectTimes
						.Where( kvp => (now - kvp.Value).TotalSeconds > DisconnectGraceSeconds )
						.Select( kvp => kvp.Key )
						.ToList();

					if(expired.Count > 0)
					{
						ADLog.Write( $"Removing {expired.Count} accounts after timeout" );

						MainGrid?.Dispatcher.Invoke( () =>
						{
							foreach(var name in expired)
							{
								var row = Rows.FirstOrDefault( r => r.AccountName.Equals( name, StringComparison.OrdinalIgnoreCase ) );
								if(row != null)
								{
									row.Acct.AccountItemUpdate -= OnAccountItemUpdate;
									row.Acct.PositionUpdate -= OnPositionUpdate;
									row.Acct.ExecutionUpdate -= OnExecutionUpdate;
									Rows.Remove( row );
								}
								disconnectTimes.Remove( name );
							}
							RecalcSummaries();
							//View.Refresh();
						} );

						//Rows = new ObservableCollection<AccountRow>( Rows.Where( r => !expired.Contains( r.AccountName ) ) );					
						//View = CollectionViewSource.GetDefaultView( Rows );
						//View.Refresh();
						//foreach(var name in expired)
						//disconnectTimes.Remove( name );
					}
				}
				else
				{
					InitializeAccountsDiff();

					if(CopierMgr.IsActive)
						CopierMgr.RefreshAccountsAfterReconnect();

					if(CopierMgr.Master != null &&
						!Account.All.Any( a => a.Name.Equals( CopierMgr.Master.Name, StringComparison.OrdinalIgnoreCase ) ))
					{
						ADLog.Write( "Master disconnected — clearing copier." );
						CopierMgr.ClearAll();
					}

					MainGrid?.Dispatcher.InvokeAsync( () =>
					{
						foreach(var r in Rows.Where( x => added.Contains( x.AccountName ) ))
						{
							PulseRow( r, ADTheme.ConnectRow );
						}
					}, DispatcherPriority.Render );
				}

				prevAccounts = connected;

				//MainGrid?.Dispatcher.InvokeAsync( () =>
				//Application.Current?.Dispatcher.InvokeAsync( () =>
				//{
					View.Refresh();
				//}, DispatcherPriority.Normal );
			}
			catch(Exception ex)
			{
				//Print( $"CheckConnections error: {ex.Message}" );
				ADLog.Write( $"CheckConnections error: {ex.Message}" );
			}
		}


		void CheckConnections__()
		{
			try
			{
				foreach(var r in Rows)
				{
					if(r.Acct == null) continue;
					r.OnChanged( nameof( r.ConnStatus ) );
				}

				// current live connected accounts
				var live = Account.All
					.Where( a => a.ConnectionStatus == ConnectionStatus.Connecting || a.ConnectionStatus == ConnectionStatus.Connected )
					.Select( a => a.Name )
					.ToHashSet( StringComparer.OrdinalIgnoreCase );

				addedAccounts = live.Except(prevAccounts).ToHashSet( StringComparer.OrdinalIgnoreCase);
				var added = live.Except( prevAccounts ).ToList();
				var removed = prevAccounts.Except( live ).ToList();

				// nothing changed
				if(added.Count == 0 && removed.Count == 0)
					return;

				//Print( $"Account list changed: +{added.Count}  -{removed.Count}" );
				ADLog.Write( $"Account list changed: +{added.Count}  -{removed.Count}" );

				void DoReinitAndPulseAdds()
				{
					//Print( "DoReinitAndPulseAdds" );

					InitializeAccountsDiff();
					//UninitializeAccounts();
					//InitializeAccounts();

					Application.Current?.Dispatcher.InvokeAsync( () =>
					//SafeDispatchAsync( () =>
					{
						foreach(var r in Rows.Where( x => added.Contains( x.AccountName ) ))
						{
							PulseRow( r, ADTheme.ConnectRow );
						}
					}, DispatcherPriority.Render );

					//Application.Current?.Dispatcher.InvokeAsync( () => View.Refresh() );
					View.Refresh();
				}

				if(removed.Count > 0)
				{
					var toRemove = Rows.Where( x => removed.Contains( x.AccountName ) ).ToList();
					if(toRemove.Count > 0)
					{
						var last = toRemove.Last();
						foreach(var r in toRemove)
						{
							if(r == last)
								PulseRow( r, ADTheme.DisconnectRow, DoReinitAndPulseAdds );
							else
								PulseRow( r, ADTheme.DisconnectRow );
						}
					}
					else
					{
						DoReinitAndPulseAdds();
					}
				}
				else
				{
					// only additions
					DoReinitAndPulseAdds();
				}

				prevAccounts = live;

				Application.Current?.Dispatcher.InvokeAsync( () =>
				//SafeDispatchAsync( () =>
				{
					View.Refresh();
				}, DispatcherPriority.Normal );
			}
			catch(Exception ex)
			{
				Print( $"CheckConnections error: {ex.Message}" );
				ADLog.Write( $"CheckConnections error: {ex.Message}" );
			}
		}

		void PulseRow( AccountRow row, Color color, Action onCompleted = null )
		{
			try
			{
				if(row.RowRef == null)
				{
					ADLog.Write( $"PulseRow: no row reference for {row.AccountName}" );
					return;
				}

				var dgr = row.RowRef;
				var baseColor = ((SolidColorBrush)ADTheme.RowBg).Color;
				var brush = new SolidColorBrush( baseColor );
				dgr.Background = brush;

				var anim = new ColorAnimation
				{
					From = color,
					To = baseColor,
					Duration = TimeSpan.FromMilliseconds( 66 * 3 ),
					AutoReverse = false
				};

				if(onCompleted != null)
				{
					anim.Completed += ( s, e ) => onCompleted();
				}

				brush.BeginAnimation( SolidColorBrush.ColorProperty, anim );
			}
			catch(Exception ex)
			{
				Print( $"PulseRow error: {ex.Message}" );
				//ADLog.Write( $"PulseRow error: {ex.Message}" );
			}
		}


		// Risk enforcement entrypoint (guarded by RiskEnabled)
		void EnforceRisk(AccountRow r)
        {
            if (!r.RiskEnabled) return;

            if (r.TotalPnL >= r.DailyGoal)
            {
				r.Safe( () => {
					FlattenAccount( r.Acct );
				});
				Print( $"Risk flatten (DailyGoal) {r.AccountName}" );
				//ADLog.Write($"Risk flatten (DailyGoal) {r.AccountName}");
            }
            if (r.TotalPnL <= r.DailyLoss)
            {
				r.Safe( () => {
					FlattenAccount( r.Acct );
				} );
				Print( $"Risk flatten (DailyLoss) {r.AccountName}" );
				//ADLog.Write($"Risk flatten (DailyLoss) {r.AccountName}");
			}
            /*if (r.AutoLiquidate > 0 && r.NetLiq <= r.AutoLiquidate)
            {
				r.Safe( () => {
					FlattenAccount( r.Acct );
				} );
				ADLog.Write($"Risk flatten (AutoLiq) {r.AccountName}");
            }*/
        }

		void FlattenAccount( Account acct )
		{
			if(acct == null)
				return;

			try
			{
				// Cancel all working orders
				acct.Cancel( acct.Orders );

				// Collect instruments with open positions
				var instruments = acct.Positions
					.Select( p => p.Instrument )
					.Distinct()
					.ToList();

				// Flatten those instruments
				if(instruments.Count > 0)
					acct.Flatten( instruments );
			}
			catch(Exception ex)
			{
				Print( $"FlattenAccount error [{acct.Name}]: {ex.Message}" );
				ADLog.Write( $"FlattenAccount error [{acct.Name}]: {ex.Message}" );
			}
		}

		// Role button logic per your rules
		void OnRoleButtonClicked(AccountRow clicked)
        {
            var master = Rows.FirstOrDefault(r => r.Role == RowRole.Master);

            if (master == null) 
			{
				clicked.Role = RowRole.Master;
				clicked.Size = 1;      // reset multiplier to 1x
				CopierMgr.SetMaster( clicked.Acct );
				//NinjaTrader.Code.Output.Process( $"Role change -> {clicked.AccountName} now {clicked.Role}", PrintTo.OutputTab1 );
				return; 
			}

            if (clicked.Role == RowRole.Master)
            {
				//NinjaTrader.Code.Output.Process( $"Clear all Roles", PrintTo.OutputTab1 );

				foreach(var r in Rows)
				{
					r.Role = RowRole.None; // clear all
				}
				CopierMgr.ClearAll();
                return;
            }

            if (clicked.Role == RowRole.Follower)
            {
				clicked.Role = RowRole.None; // toggle off
				//NinjaTrader.Code.Output.Process( $"Role change -> {clicked.AccountName} now {clicked.Role}", PrintTo.OutputTab1 );
				CopierMgr.RemoveFollower( clicked.Acct );
                return;
            }

            if (clicked.Role == RowRole.None && master != clicked)
            {
				clicked.Role = RowRole.Follower; // opt-in follower
				CopierMgr.AddFollower( clicked.Acct );
				//NinjaTrader.Code.Output.Process( $"Role change -> {clicked.AccountName} now {clicked.Role}", PrintTo.OutputTab1 );
            }
        }

        // Summaries (group by connection + TOTAL)
        internal void RecalcSummaries()
        {
            try
            {
                var visible = Rows
							.Where(r => !r.Hidden)
							//.Where( r => !r.AccountName.StartsWith("Sim",StringComparison.InvariantCultureIgnoreCase) )
							.Where( r => !showSim ? !r.AccountName.StartsWith( "Sim", StringComparison.InvariantCultureIgnoreCase ) : true )
							.ToList();
                var groups = visible.GroupBy(r => r.ConnectionName).OrderBy( g => g.Key, StringComparer.OrdinalIgnoreCase );

				Summaries.Clear();

                foreach (var g in groups)
                {
					var longs = g.Count( x => x.Dir == MarketPosition.Long );
					var shorts = g.Count( x => x.Dir == MarketPosition.Short );
					var LDir = longs > shorts ? MarketPosition.Long :
						  shorts > longs ? MarketPosition.Short :
						  MarketPosition.Flat;
					LDir = longs > 0 && shorts > 0 ? (MarketPosition)(-999) : LDir;

					Summaries.Add(new SummaryRow
                    {
                        Label = g.Key,
                        AccountCount = g.Count(),
						Dir = LDir,
						//Dir = g.Any( x => x.Dir == MarketPosition.Long ) ? MarketPosition.Long : g.Any( x => x.Dir == MarketPosition.Short ) ? MarketPosition.Short : MarketPosition.Flat,
						//TotalSize = g.Sum( x => x.Size ),
						TotalPos = g.Sum( x => x.Pos ),
						TotalUnrealized = g.Sum(x => x.Unrealized),
                        TotalRealized = g.Sum(x => x.Realized),
                        TotalComms = g.Sum(x => x.Commissions),
                        TotalCash = g.Sum(x => x.CashValue),
                        TotalNetLiq = g.Sum(x => x.NetLiq)
                    });
                }


				//Print( "Total" + visible.FirstOrDefault().Dir );
				Summaries.Add(new SummaryRow
                {
                    Label = "TOTAL",
                    AccountCount = visible.Count,
					Dir = MarketPosition.Flat,
					//TotalSize = visible.Sum( x => x.Size ),
					TotalPos = visible.Sum( x => x.Pos ),
					TotalUnrealized = visible.Sum(x => x.Unrealized),
                    TotalRealized = visible.Sum(x => x.Realized),
                    TotalComms = visible.Sum(x => x.Commissions),
                    TotalCash = visible.Sum(x => x.CashValue),
                    TotalNetLiq = visible.Sum(x => x.NetLiq)
                });
            }
            catch (Exception ex)
            {
                ADLog.Write($"RecalcSummaries error: {ex.Message}");
            }
        }

        // Event handlers with noise filtering and async UI dispatch
        const double EPS = 0.01;

		void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
			Account acct = sender as Account; 
			if (acct == null) return;
            
			AccountRow row = Rows.FirstOrDefault(r => r.Acct == acct); 
			if (row == null) return;

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                try
                {
                    switch (e.AccountItem)
                    {
						case AccountItem.UnrealizedProfitLoss:
							//if(Math.Abs( e.Value - row.LastUnrl ) > EPS)
							{
								row.Unrealized = e.Value;
								row.LastUnrl = e.Value;
							}
							break;
						case AccountItem.CashValue:
							//if (Math.Abs(e.Value - row.LastCash) > EPS) 
							{
								row.CashValue = e.Value; 
								row.LastCash = e.Value; 
							}
                            break;
                        case AccountItem.RealizedProfitLoss:
                            //if (Math.Abs(e.Value - row.LastRealized) > EPS) 
							{ 
								row.Realized = e.Value; 
								row.LastRealized = e.Value; 
							}
                            break;
                        /*case AccountItem.Commission:
                            //if (Math.Abs(e.Value - row.LastComms) > EPS) 
							{ 
								row.Commissions = e.Value; 
								row.LastComms = e.Value; 
							}
                            break;*/
						case AccountItem.TrailingMaxDrawdown:
							//if (Math.Abs(e.Value - row.LastComms) > EPS) 
							{
								row.TrailingMaxDrawdown = e.Value;
								row.LastTrailingMaxDrawdown = e.Value;
							}
							break;
						case AccountItem.NetLiquidation:
							//if (Math.Abs(e.Value - row.LastComms) > EPS) 
							{
								row.NetLiq = e.Value;
								row.LastNet = e.Value;
							}
							break;
						default:
							break;
                    }

					double comms = SafeGet( acct, AccountItem.Commission );
					if(comms > 0)
					{
						Print( "On Item Update Comms: " + row.Commissions );
						row.Commissions = comms;
					}

					/*var newNet = row.CashValue + row.Unrealized;
					//if(Math.Abs( newNet - row.LastNet ) > EPS)
					{
						row.NetLiq = newNet;
						row.LastNet = newNet;
					}*/
					var autoLiq = row.CashValue - row.TrailingMaxDrawdown;
					//if(Math.Abs( newNet - row.LastNet ) > EPS)
					{
						row.AutoLiquidate = autoLiq;
						//row.LastAutoLiquidate = autoLiq;
					}

					//RecalcSummaries();

					EnforceRisk(row);
                }
                catch (Exception ex) 
				{ 
					//Print( $"OnAccountItemUpdate UI error: {ex.Message}" );
					ADLog.Write($"OnAccountItemUpdate UI error: {ex.Message}"); 
				}
            }, DispatcherPriority.Normal);
        }


		private void OnPositionUpdate( object sender, PositionEventArgs e )
		{
			Account acct = sender as Account; 
			if(acct == null) return;
			
			var row = Rows.FirstOrDefault( r => r.Acct == acct ); 
			if(row == null) return;

			//CopierMgr?.HandleExecutionUpdate( acct, e );
			//try { Copier_OnExecutionUpdate( acct, e ); }
			//catch(Exception ex) { ADLog.Write( $"OnExecutionUpdate error: {ex.Message}" ); }

			try
			{
				double unrl = 0;
				if(acct.Positions != null && acct.Positions.Count > 0)
				{
					unrl = acct.Positions.Sum( p => p.GetUnrealizedProfitLoss( PerformanceUnit.Currency ) );
					int totalQty = acct.Positions.Sum( p => Math.Abs( p.Quantity ) );
					var hasLong = acct.Positions.Any( p => p.MarketPosition == MarketPosition.Long );
					var hasShort = acct.Positions.Any( p => p.MarketPosition == MarketPosition.Short );

					row.Pos = totalQty;
					row.Qty = totalQty;
					row.Dir = hasLong && hasShort ? (MarketPosition)(-999) :
							  hasLong ? MarketPosition.Long :
							  hasShort ? MarketPosition.Short :
							  MarketPosition.Flat;
				}
				else
				{
					row.Dir = MarketPosition.Flat;
					row.Pos = 0;
					row.Qty = 0;
				}
				/*var pos = acct.Positions?.FirstOrDefault( p => p.Instrument == e.Position.Instrument );
				if(pos != null)
				{
					row.Dir = pos.MarketPosition;
					//row.Pos = row.Dir == MarketPosition.Long ? pos.Quantity : -pos.Quantity;
					row.Pos = Math.Abs( pos.Quantity );
					row.Qty = Math.Abs( pos.Quantity );
				}
				else
				{
					row.Dir = MarketPosition.Flat;
					row.Pos = 0;
					row.Qty = 0;
				}*/

				//try { if(pos != null) unrl = pos.GetUnrealizedProfitLoss( PerformanceUnit.Currency ); } catch { }
				if(Math.Abs( unrl - row.LastUnrl ) > EPS) 
				{ 
					row.Unrealized = unrl; row.LastUnrl = unrl; 
				}

				var newNet = row.CashValue + row.Unrealized;
				if(Math.Abs( newNet - row.LastNet ) > EPS) 
				{ 
					row.NetLiq = newNet; row.LastNet = newNet; 
				}

				//EnforceRisk( row );

				//RecalcSummaries();

				// Force UI update
				Application.Current?.Dispatcher?.InvokeAsync( () =>
				{
					View.Refresh();
				}, DispatcherPriority.Normal );
				//View.Refresh();
			}
			catch(Exception ex) 
			{
				Print( $"OnPositionUpdate UI error: {ex.Message}" );
				//ADLog.Write( $"OnPositionUpdate UI error: {ex.Message}" ); 
			}
		}

		private void OnExecutionUpdate( object sender, ExecutionEventArgs e )
		{
			if(e.Execution.Commission > 0)
				Print( "OnExecution Comms: " + e.Execution.Commission );
		}


		double SafeGet( Account a, AccountItem item )
		{
			try
			{
				double val = a.GetAccountItem( item, Currency.UsDollar ).Value;
				//Print( "Try Get AccountItem: " + item.ToString() + " - " + val );
				return val;
			}
			catch
			{
				//Print( "ERROR: SafeGet failed. Return 0" );
				return 0;
			}
		}

		void SafeDispatch( Action action, DispatcherPriority priority )
		{
			if(action == null)
				return;

			var disp = Application.Current?.Dispatcher;
			if(disp == null)
			{
				action();
				return;
			}

			if(disp.CheckAccess())
				action();
			else
				disp.Invoke( action, priority );
		}

		void SafeDispatchAsync( Action action, DispatcherPriority priority = DispatcherPriority.Background )
		{
			if(action == null)
				return;

			var disp = Application.Current?.Dispatcher;
			if(disp == null)
			{
				action();
				return;
			}

			if(disp.CheckAccess())
				action();
			else
				disp.InvokeAsync( action, priority );
		}


		// Persistence (JSON via Newtonsoft)
		internal void SaveSettings()
		{
			try
			{
				var model = new PersistModel
				{
					WindowLeft = dash.Left,
					WindowTop = dash.Top,
					WindowWidth = dash.Width,
					WindowHeight = dash.Height,
					ShowSim = showSim,
					CopierEnabled = copierEnabled,
					GroupByConnectionEnabled = groupByConnectionEnabled,
					Rows = Rows.Select( r => new PersistModel.RowCfg
					{
						Account = r.AccountName,
						Role = (int)r.Role,
						Hidden = r.Hidden,
						RiskEnabled = r.RiskEnabled,
						Size = r.Size,
						DailyGoal = r.DailyGoal,
						DailyLoss = r.DailyLoss,
						AutoLiq = r.AutoLiquidate,
						StartBalance = r.StartBalance,
						DayRealizedStart = r.DayRealizedStart
					} ).ToArray(),
					Columns = MainGrid?.Columns.Select( c => new PersistModel.ColumnCfg
					{
						Header = c.Header?.ToString(),
						Width = c.ActualWidth,
						DisplayIndex = c.DisplayIndex,
						Visible = c.Visibility == Visibility.Visible,
						IsSorted = c.SortDirection.HasValue,
						SortDirection = c.SortDirection
					} ).ToList()
				};

				Print( "Save settings: " + SettingsPath );

				Directory.CreateDirectory( Path.GetDirectoryName( SettingsPath ) );
				File.WriteAllText( SettingsPath, JsonConvert.SerializeObject( model, Formatting.Indented ) );
				ADLog.Write( $"Settings saved: {SettingsPath}" );
			}
			catch(Exception ex)
			{
				ADLog.Write( $"SaveSettings error: {ex.Message}" );
			}
		}

		internal void LoadSettings()
		{
			try
			{
				if(!File.Exists( SettingsPath )) return;

				Print( "Load settings: " + SettingsPath );

				var model = JsonConvert.DeserializeObject<PersistModel>( File.ReadAllText( SettingsPath ) );
				if(model == null) return;

				dash.Left = model.WindowLeft;
				dash.Top = model.WindowTop;
				dash.Width = model.WindowWidth;
				dash.Height = model.WindowHeight;

				showSim = model.ShowSim;
				copierEnabled = model.CopierEnabled;
				groupByConnectionEnabled = model.GroupByConnectionEnabled;

				foreach(var cfg in model.Rows)
				{
					var row = Rows.FirstOrDefault( r => r.AccountName.Equals( cfg.Account, StringComparison.OrdinalIgnoreCase ) );
					if(row == null) continue;
					row.Role = (RowRole)cfg.Role;
					row.Hidden = cfg.Hidden;
					row.RiskEnabled = cfg.RiskEnabled;
					row.Size = cfg.Size;
					row.DailyGoal = cfg.DailyGoal;
					row.DailyLoss = cfg.DailyLoss;
					row.AutoLiquidate = cfg.AutoLiq;
					row.StartBalance = cfg.StartBalance;
					row.DayRealizedStart = cfg.DayRealizedStart;
				}

				if(model.Columns != null && MainGrid != null)
				{
					foreach(var colCfg in model.Columns)
					{
						var col = MainGrid.Columns.FirstOrDefault( c => c.Header?.ToString() == colCfg.Header );
						if(col == null) continue;
						col.Width = new DataGridLength( colCfg.Width );
						col.DisplayIndex = colCfg.DisplayIndex;
						col.Visibility = colCfg.Visible ? Visibility.Visible : Visibility.Collapsed;
						col.SortDirection = colCfg.SortDirection;
					}

					MainGrid.Items.SortDescriptions.Clear();
					foreach(var colCfg in model.Columns.Where( c => c.IsSorted ))
					{
						var dir = colCfg.SortDirection ?? ListSortDirection.Ascending;
						MainGrid.Items.SortDescriptions.Add( new SortDescription( colCfg.Header, dir ) );
					}
				}

				ADLog.Write( "Settings restored successfully." );
			}
			catch(Exception ex)
			{
				ADLog.Write( $"LoadSettings error: {ex.Message}" );
			}
		}
	}
}
