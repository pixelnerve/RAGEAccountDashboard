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
                public int Role;           // RowRole
                public bool Hidden;
                public bool RiskEnabled;
                public int Size;
                public double DailyGoal;
                public double DailyLoss;
                public double AutoLiq;
                public double StartBalance;
                public double DayRealizedStart;
            }
            public RowCfg[] Rows;
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
					"NinjaTrader 8", "templates", "AccountDashboard", "settings.json" );

				LogPath = Path.Combine(
					Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ),
					"NinjaTrader 8", "log", "AccountDashboard.log" );

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


		internal void InitializeAccounts()
		{
			// cache roles before reset
			var savedRoles = Rows?.ToDictionary( r => r.AccountName, r => new { r.Role, r.Size } )
							 ?? new();

			/*if(Rows == null)
				Rows = new ObservableCollection<AccountRow>();
			if(Summaries == null)
				Summaries = new ObservableCollection<SummaryRow>();*/

			CopierMgr.ClearAll();
			Rows.Clear();
			Summaries.Clear();

			foreach(var a in Account.All)
			{
				if(a.ConnectionStatus != ConnectionStatus.Connected)
					continue;

				a.PositionUpdate += OnPositionUpdate;
				a.AccountItemUpdate += OnAccountItemUpdate;
				a.ExecutionUpdate += OnExecutionUpdate;
				//a.OrderUpdate += OnOrderUpdate;
					
				//Print( $"Add new row {a.Name}");
				var row = new AccountRow( a, canChangeRole: _ => true, onRoleClick: OnRoleButtonClicked );

				// restore previous role and size if available
				if(savedRoles.TryGetValue( a.Name, out var saved ))
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
				row.CashValue = SafeGet( a, AccountItem.CashValue );
				row.Realized = SafeGet( a, AccountItem.RealizedProfitLoss );
				row.Unrealized = SafeGet( a, AccountItem.UnrealizedProfitLoss );
				row.Commissions = SafeGet( a, AccountItem.Commission );
				row.TrailingMaxDrawdown = SafeGet( a, AccountItem.TrailingMaxDrawdown );

				row.StartBalance = SafeGet( a, AccountItem.CashValue );
				row.DayRealizedStart = SafeGet( a, AccountItem.RealizedProfitLoss );

				// --- existing position at startup ---
				var pos = a.Positions?.FirstOrDefault();
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
				}
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
			if(refreshTimer == null)
			{
				refreshTimer = new DispatcherTimer( DispatcherPriority.Background )
				{ Interval = TimeSpan.FromMilliseconds( 666 ) };
				refreshTimer.Tick += ( s, e ) => RecalcSummaries();
				refreshTimer.Start();
			}
			// --- connection monitor ---
			if(connectionTimer == null)
			{
				connectionTimer = new DispatcherTimer( DispatcherPriority.Background )
				{
					Interval = TimeSpan.FromSeconds( 1 )
				};
				connectionTimer.Tick += ( s, e ) => CheckConnections();
				connectionTimer.Start();
			}
		}

		internal void UninitializeAccounts()
		{
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

			CopierMgr.ClearAll();
			foreach(var r in Rows)
			{
				r.Acct.PositionUpdate -= OnPositionUpdate;
				r.Acct.AccountItemUpdate -= OnAccountItemUpdate;
				r.Acct.ExecutionUpdate -= OnExecutionUpdate;
				//r.Acct.OrderUpdate -= OnOrderUpdate;

			}
		}

		void CheckConnections()
		{
			try
			{
				// current live connected accounts
				var live = Account.All
					.Where( a => a.ConnectionStatus == ConnectionStatus.Connected )
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
					InitializeAccounts();
					Application.Current?.Dispatcher.Invoke( () =>
					//SafeDispatchAsync( () =>
					{
						foreach(var r in Rows.Where( x => added.Contains( x.AccountName ) ))
						{
							PulseRow( r, ADTheme.ConnectRow );
						}
					}, DispatcherPriority.Normal );
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
				ADLog.Write( $"PulseRow error: {ex.Message}" );
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
				//r.Safe(() => Account.FlattenEverything());
				ADLog.Write($"Risk flatten (DailyGoal) {r.AccountName}");
            }
            if (r.TotalPnL <= r.DailyLoss)
            {
				r.Safe( () => {
					FlattenAccount( r.Acct );
				} );
				//r.Safe(() => r.Acct.FlattenEverything());
				ADLog.Write($"Risk flatten (DailyLoss) {r.AccountName}");
            }
            if (r.AutoLiquidate > 0 && r.NetLiq <= r.AutoLiquidate)
            {
				r.Safe( () => {
					FlattenAccount( r.Acct );
				} );
				//r.Safe(() => r.Acct.FlattenEverything());
				ADLog.Write($"Risk flatten (AutoLiq) {r.AccountName}");
            }
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
                var groups = visible.GroupBy(r => r.ConnectionName);

                Summaries.Clear();

                foreach (var g in groups)
                {
                    Summaries.Add(new SummaryRow
                    {
                        Label = g.Key,
                        AccountCount = g.Count(),
						Dir = g.FirstOrDefault().Dir,
						//TotalSize = g.Sum( x => x.Size ),
						TotalPos = g.Sum( x => x.Pos ),
						TotalUnrealized = g.Sum(x => x.Unrealized),
                        TotalRealized = g.Sum(x => x.Realized),
                        TotalComms = g.Sum(x => x.Commissions),
                        TotalCash = g.Sum(x => x.CashValue),
                        TotalNetLiq = g.Sum(x => x.NetLiq)
                    });
                }

				Summaries.Add(new SummaryRow
                {
                    Label = "TOTAL",
                    AccountCount = visible.Count,
					Dir = MarketPosition.Flat, // visible.FirstOrDefault().Dir,
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
                        case AccountItem.Commission:
                            //if (Math.Abs(e.Value - row.LastComms) > EPS) 
							{ 
								row.Commissions = e.Value; 
								row.LastComms = e.Value; 
							}
                            break;
						case AccountItem.TrailingMaxDrawdown:
							//if (Math.Abs(e.Value - row.LastComms) > EPS) 
							{
								row.TrailingMaxDrawdown = e.Value;
								row.LastTrailingMaxDrawdown = e.Value;
							}
							break;
						default:
							break;
                    }
					var newNet = row.CashValue + row.Unrealized;
					//if(Math.Abs( newNet - row.LastNet ) > EPS)
					{
						row.NetLiq = newNet;
						row.LastNet = newNet;
					}
					var autoLiq = row.CashValue - row.TrailingMaxDrawdown;
					//if(Math.Abs( newNet - row.LastNet ) > EPS)
					{
						row.AutoLiquidate = autoLiq;
						//row.LastAutoLiquidate = autoLiq;
					}

					EnforceRisk( row);
                }
                catch (Exception ex) 
				{ 
					Print( $"OnAccountItemUpdate UI error: {ex.Message}" );
					ADLog.Write($"OnAccountItemUpdate UI error: {ex.Message}"); 
				}
            }, DispatcherPriority.Normal);
        }


		private void OnPositionUpdate( object sender, PositionEventArgs e )
		{
			Account acct = sender as Account; if(acct == null) return;
			var row = Rows.FirstOrDefault( r => r.Acct == acct ); if(row == null) return;

			//CopierMgr?.HandleExecutionUpdate( acct, e );
			//try { Copier_OnExecutionUpdate( acct, e ); }
			//catch(Exception ex) { ADLog.Write( $"OnExecutionUpdate error: {ex.Message}" ); }

			Application.Current?.Dispatcher?.Invoke( () =>
			{
				try
				{
					var pos = acct.Positions?.FirstOrDefault( p => p.Instrument == e.Position.Instrument );
					if(pos != null)
					{
						row.Dir = pos.MarketPosition;
						row.Pos = row.Dir == MarketPosition.Long ? pos.Quantity : -pos.Quantity;
						//row.Pos = Math.Abs( pos.Quantity );
						row.Qty = Math.Abs( pos.Quantity );
					}
					else
					{
						row.Dir = MarketPosition.Flat;
						row.Pos = 0;
						row.Qty = 0;
					}

					double unrl = 0;
					try { if(pos != null) unrl = pos.GetUnrealizedProfitLoss( PerformanceUnit.Currency ); } catch { }
					if(Math.Abs( unrl - row.LastUnrl ) > EPS) { row.Unrealized = unrl; row.LastUnrl = unrl; }

					var newNet = row.CashValue + row.Unrealized;
					if(Math.Abs( newNet - row.LastNet ) > EPS) { row.NetLiq = newNet; row.LastNet = newNet; }

					EnforceRisk( row );

					// Force UI update
					View.Refresh();
				}
				catch(Exception ex) { ADLog.Write( $"OnPositionUpdate UI error: {ex.Message}" ); }
			}, DispatcherPriority.Normal );
		}


		void OnExecutionUpdate__( object sender, ExecutionEventArgs e )
		{
			if(e == null || e.Execution == null)
				return;

			try
			{
				var acct = sender as Account;
				var row = Rows.FirstOrDefault( r => r.Acct == acct );
				if(row == null)
					return;

				// Recalculate position quantity and direction
				var pos = acct.Positions?.FirstOrDefault( p => p.Instrument == e.Execution.Instrument );
				if(pos == null || pos.Quantity == 0)
				{
					row.Dir = MarketPosition.Flat;
					row.Pos = 0;
					//row.Qty = 0;
				}
				else
				{
					row.Dir = pos.MarketPosition;
					row.Pos = Math.Abs( pos.Quantity );
					//row.Qty = Math.Abs( pos.Quantity );
				}

				//Print( acct.Name + " -- " + row.Dir + ", " + row.Pos + ", " + row.Qty );

				// Force UI update
				Application.Current.Dispatcher.Invoke( () => View.Refresh() );
			}
			catch(Exception ex)
			{
				ADLog.Write( $"OnExecutionUpdate error: {ex.Message}" );
			}
		}

		void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            /*Account acct = sender as Account; if (acct == null) return;
            var row = Rows.FirstOrDefault(r => r.Acct == acct); if (row == null) return;

			//CopierMgr?.HandleExecutionUpdate( acct, e );
			//try { Copier_OnExecutionUpdate( acct, e ); }
			//catch(Exception ex) { ADLog.Write( $"OnExecutionUpdate error: {ex.Message}" ); }

			Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
					var pos = acct.Positions?.FirstOrDefault(p => p.Instrument == e.Execution.Instrument);
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
					}

                    double unrl = 0;
                    try { if (pos != null) unrl = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency); } catch { }
                    if (Math.Abs(unrl - row.LastUnrl) > EPS) { row.Unrealized = unrl; row.LastUnrl = unrl; }

                    var newNet = row.CashValue + row.Unrealized;
                    if (Math.Abs(newNet - row.LastNet) > EPS) { row.NetLiq = newNet; row.LastNet = newNet; }

                    EnforceRisk(row);

					// Force UI update
					View.Refresh();
				}
				catch (Exception ex) { ADLog.Write($"OnExecutionUpdate UI error: {ex.Message}"); }
            }, DispatcherPriority.Background);*/
        }

        // Copying of market orders will be handled in the Copier partial (stubbed here to keep signatures)
        /*void OnOrderUpdate(object sender, OrderEventArgs e)
        {
			Account acct = sender as Account;

			//Print( "ON ORDER CORE " + acct.Name );
			//if( acct == CopierMgr.Master )
				//CopierMgr?.HandleOrderUpdate( sender as Account, e );
			//try { Copier_OnOrderUpdate(sender as Account, e); }
            //catch (Exception ex) { ADLog.Write($"OnOrderUpdate error: {ex.Message}"); }
        }*/

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
                var data = new PersistModel
                {
                    Rows = Rows.Select(r => new PersistModel.RowCfg
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
                    }).ToArray()
                };
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                ADLog.Write("Settings saved.");
            }
            catch (Exception ex) { ADLog.Write($"SaveSettings error: {ex.Message}"); }
        }

        internal void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var data = JsonConvert.DeserializeObject<PersistModel>(File.ReadAllText(SettingsPath));
                if (data?.Rows == null) return;

                foreach (var cfg in data.Rows)
                {
                    var r = Rows.FirstOrDefault(x => x.AccountName == cfg.Account);
                    if (r == null) continue;
                    r.Role = (RowRole)cfg.Role;
                    r.Hidden = cfg.Hidden;
                    r.RiskEnabled = cfg.RiskEnabled;
                    r.Size = cfg.Size;
                    r.DailyGoal = cfg.DailyGoal;
                    r.DailyLoss = cfg.DailyLoss;
                    r.AutoLiquidate = cfg.AutoLiq;
                    r.StartBalance = cfg.StartBalance;
                    r.DayRealizedStart = cfg.DayRealizedStart;
                }
                ADLog.Write("Settings loaded.");
            }
            catch (Exception ex) { ADLog.Write($"LoadSettings error: {ex.Message}"); }
        }
    }
}
