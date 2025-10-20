#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.HotKeys;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies.LIVESIM;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;

#endregion


namespace NinjaTrader.AddOns
{
    // Stub for future copier logic. Kept separate for clarity.
    public partial class AccountDashboard : AddOnBase
    {

		internal class Copier
		{
			internal Account Master;
			internal readonly List<Account> Followers = new();

			// Internal
			//private List<string> clientAccounts;
			//private Dictionary<string, Account> accountCache = new();
			//private Account masterAccount = null;

			private readonly Dictionary<string, List<Order>> mirrorMap = new Dictionary<string, List<Order>>();
			private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
			private bool logRunning = false;

			public OrderTypeOverrideMode OrderTypeOverride { get; set; } = OrderTypeOverrideMode.SameAsMaster;
			public UpdateMode OrderUpdateMode { get; set; } = UpdateMode.Change;
			public bool EnableLogging { get; set; } = true;

			internal bool IsActive => Master != null && Followers.Count > 0;

			public Copier()
			{
				StartLogProcessor();
			}

			// ---------- Role management ----------

			internal void SetMaster( Account acct )
			{
				UnsubscribeMaster();
				Master = acct;
				Followers.Clear();

				if(Master == null) return;

				Master.OrderUpdate += OnOrderUpdate;
				//Master.ExecutionUpdate += OnExecutionUpdate;
				Log( $"Master set to {Master.Name}" );
				ADLog.Write( $"Master set to {Master.Name}" );
			}

			internal void AddFollower( Account acct )
			{
				if(acct == null || acct == Master || Followers.Contains( acct )) return;
				Followers.Add( acct );
				//accountCache.Add( acct.Name, acct );
				Log( $"Follower added: {acct.Name}" );
				ADLog.Write( $"Follower added: {acct.Name}" );
			}

			internal void RemoveFollower( Account acct )
			{
				if(Followers.Remove( acct ))
				{
					//accountCache.Remove( acct.Name );
					Log( $"Follower removed: {acct.Name}" );
					ADLog.Write( $"Follower removed: {acct.Name}" );
				}
			}

			internal void ClearAll()
			{
				try
				{
					foreach(var kvp in mirrorMap.ToArray())
						CancelMirroredOrders( kvp.Key );
				}
				catch
				{
					Log( "(State:Terminated)  Failed to run CancelMirroredOrders" );
				}

				mirrorMap.Clear();

				UnsubscribeMaster();
				Master = null;
				Followers.Clear();
				//accountCache.Clear();
				//Log( "Copier cleared." );
				ADLog.Write( "Copier cleared." );
			}

			private void UnsubscribeMaster()
			{
				if(Master == null) return;
				try
				{
					Master.OrderUpdate -= OnOrderUpdate;
					//Master.ExecutionUpdate -= OnExecutionUpdate;
				}
				catch { }
			}

			// ---------- Master event hooks ----------

			/*internal void HandleExecutionUpdate( Account src, ExecutionEventArgs e )
			{
				if(Master == null || src != Master || !IsActive)
					return;

				OnExecutionUpdate( src, e );
			}*/

			internal void HandleOrderUpdate( Account src, OrderEventArgs e )
			{
				if(Master == null || src != Master || !IsActive)
					return;

				OnOrderUpdate( src, e );
			}

			private void OnOrderUpdate( object sender, OrderEventArgs e )
			{
				//if(Master == null) return;

				Log( "ON ORDER COPIER " + (sender as Account).Name );

				var order = e.Order;
				if(order == null)
				{
					Log( "ERROR: Something wrong with the order. It's null" );
					return;
				}
				if(!order.Account.Name.Equals( Master.Name, StringComparison.OrdinalIgnoreCase ))
				{
					Log( "ERROR: Something wrong with the order account name. Not master account: " + order.Account.Name );
					return;
				}

				Log( "order.OrderState: " + order.OrderState + ", " + order.Account.Name );

				string masterKey = order.OrderId;
				switch(order.OrderState)
				{
					case OrderState.Submitted:
						if(!mirrorMap.ContainsKey( masterKey ))
						{
							MirrorOrderToClients( masterKey, order );
							Log( $"[MASTER {order.OrderState}] {order.Instrument.FullName} {order.OrderAction} {order.Quantity}" );
						}
						else
						{
							UpdateMirroredOrders( masterKey, order );
						}
						break;
					case OrderState.Working:
						if(!mirrorMap.ContainsKey( masterKey ))
						{
							MirrorOrderToClients( masterKey, order );
							Log( $"[MASTER {order.OrderState}] {order.Instrument.FullName} {order.OrderAction} {order.Quantity}" );
						}
						else
						{
							UpdateMirroredOrders( masterKey, order );
						}
						break;
					case OrderState.Cancelled:
						if(mirrorMap.ContainsKey( masterKey ))
						{
							Log( $"[MASTER CANCELLED] {order.Instrument.FullName} {order.OrderAction} {order.Quantity}" );
							LogFollowerStates( masterKey );
							CheckFinalSync( masterKey, "Cancelled", order.Instrument.FullName, order.OrderAction, order.Quantity );
							CancelMirroredOrders( masterKey );
							mirrorMap.Remove( masterKey );
						}
						break;

					case OrderState.Rejected:
						if(mirrorMap.ContainsKey( masterKey ))
						{
							Log( $"[MASTER REJECTED] {order.Instrument.FullName} {order.OrderAction} {order.Quantity}" );
							LogFollowerStates( masterKey );
							CheckFinalSync( masterKey, "Rejected", order.Instrument.FullName, order.OrderAction, order.Quantity );
							CancelMirroredOrders( masterKey );
							mirrorMap.Remove( masterKey );
						}
						break;

					case OrderState.Filled:
						if(mirrorMap.ContainsKey( masterKey ))
						{
							Log( $"[MASTER FILLED] {order.Instrument.FullName} {order.OrderAction} {order.Quantity} {order.AverageFillPrice}" );
							LogFollowerStates( masterKey );
							CheckFinalSync( masterKey, "Filled", order.Instrument.FullName, order.OrderAction, order.Quantity );
							mirrorMap.Remove( masterKey );
						}
						break;
				}
			}

			private void MirrorOrderToClients( string masterKey, Order masterOrder )
			{
				if(!mirrorMap.ContainsKey( masterKey ))
				{
					//Print( "Add key to mirrorMap: " + masterKey );
					mirrorMap[ masterKey ] = new List<Order>();
				}

				foreach(var acct in Followers)
				{
					string acctName = acct.Name;
					//if(!accountCache.TryGetValue( client.Name, out var acct ))
						//continue;

					if(mirrorMap[ masterKey ].Any( o => o.Account == acct ))
						continue;

					try
					{
						OrderType orderType =
							OrderTypeOverride == OrderTypeOverrideMode.SameAsMaster
							? masterOrder.OrderType
							: OrderType.Market;

						string followerOco = string.IsNullOrEmpty( masterOrder.Oco )
							? null
							: $"{masterOrder.Oco}_{acct.Name}";

						var clone = acct.CreateOrder(
							masterOrder.Instrument,
							masterOrder.OrderAction,
							orderType,
							masterOrder.OrderEntry,
							masterOrder.TimeInForce,
							masterOrder.Quantity,
							//OverrideQuantity ? CustomQuantity : masterOrder.Quantity,
							masterOrder.LimitPrice,
							masterOrder.StopPrice,
							followerOco,
							masterOrder.Name,
							DateTime.Now,
							masterOrder.CustomOrder
						);

						acct.Submit( new[] { clone } );
						mirrorMap[ masterKey ].Add( clone );

						Log( $"[MIRROR] {acctName}: {clone.Instrument.FullName}, {clone.OrderAction}, Qty={clone.Quantity}, OCO={followerOco}, {orderType}" );
					}
					catch(Exception ex)
					{
						Log( $"[ERROR] Mirror submit failed for {acctName}: {ex.Message}" );
					}
				}
			}

			private void UpdateMirroredOrders( string masterKey, Order masterOrder )
			{
				if(!mirrorMap.TryGetValue( masterKey, out var followers ))
					return;

				foreach(var fo in followers)
				{
					if(OrderUpdateMode == UpdateMode.Change)
						TryChangeOrder( fo, masterOrder );
					else
						CancelAndResubmitOrder( fo, masterOrder );
				}
			}

			private void TryChangeOrder( Order followerOrder, Order masterOrder )
			{
				try
				{
					// Target qty honors the OverrideQuantity setting (keep follower fixed if enabled)
					int targetQty = masterOrder.Quantity;
					//int targetQty = OverrideQuantity ? CustomQuantity : masterOrder.Quantity;

					bool changed = false;
					double tick = followerOrder.Instrument.MasterInstrument.TickSize;

					// Quantity
					if(followerOrder.Quantity != targetQty)
					{
						followerOrder.QuantityChanged = targetQty;
						changed = true;
					}

					// Limit price (only when applicable)
					if(masterOrder.OrderType == OrderType.Limit || masterOrder.OrderType == OrderType.StopLimit)
					{
						double newLimit = Math.Round( masterOrder.LimitPrice / tick, MidpointRounding.AwayFromZero ) * tick;
						if(Math.Abs( followerOrder.LimitPrice - newLimit ) > double.Epsilon)
						{
							followerOrder.LimitPriceChanged = newLimit;
							changed = true;
						}
					}

					// Stop price (only when applicable)
					if(masterOrder.OrderType == OrderType.StopMarket || masterOrder.OrderType == OrderType.StopLimit)
					{
						double newStop = Math.Round( masterOrder.StopPrice / tick, MidpointRounding.AwayFromZero ) * tick;
						if(Math.Abs( followerOrder.StopPrice - newStop ) > double.Epsilon)
						{
							followerOrder.StopPriceChanged = newStop;
							changed = true;
						}
					}

					// OCO: NT8 does not expose an OCO*Changed property. If you must change OCO, use Cancel+Resubmit.
					// (We already handle that path in CancelAndResubmitOrder.)

					if(changed)
					{
						followerOrder.Account.Change( new[] { followerOrder } ); // <-- correct signature
						Log( $"[UPDATE] {followerOrder.Account.Name}: {followerOrder.Instrument.FullName} -> Qty={targetQty}"
						  + $"{(masterOrder.OrderType == OrderType.Limit || masterOrder.OrderType == OrderType.StopLimit ? $", Limit={followerOrder.LimitPriceChanged}" : "")}"
						  + $"{(masterOrder.OrderType == OrderType.StopMarket || masterOrder.OrderType == OrderType.StopLimit ? $", Stop={followerOrder.StopPriceChanged}" : "")}" );
					}
					else
					{
						Log( $"[UPDATE] {followerOrder.Account.Name}: No changes needed for {followerOrder.Instrument.FullName}" );
					}
				}
				catch(Exception ex)
				{
					Log( $"[ERROR] Change failed for {followerOrder.Account.Name}: {ex.Message}" );
				}
			}


			private void CancelAndResubmitOrder( Order followerOrder, Order masterOrder )
			{
				try
				{
					followerOrder.Account.Cancel( new[] { followerOrder } );

					string followerOco = string.IsNullOrEmpty( masterOrder.Oco )
						? null
						: $"{masterOrder.Oco}_{followerOrder.Account.Name}";

					var newOrder = followerOrder.Account.CreateOrder(
						masterOrder.Instrument,
						masterOrder.OrderAction,
						(OrderTypeOverride == OrderTypeOverrideMode.SameAsMaster ? masterOrder.OrderType : OrderType.Market),
						masterOrder.OrderEntry,
						masterOrder.TimeInForce,
						masterOrder.Quantity,
						//OverrideQuantity ? CustomQuantity : masterOrder.Quantity,
						masterOrder.LimitPrice,
						masterOrder.StopPrice,
						followerOco,
						masterOrder.Name,
						DateTime.Now,
						masterOrder.CustomOrder
					);

					followerOrder.Account.Submit( new[] { newOrder } );

					var list = mirrorMap[ masterOrder.OrderId ];
					list.Remove( followerOrder );
					list.Add( newOrder );

					Log( $"[UPDATE-RESUBMIT] {followerOrder.Account.Name}: Replaced order with Qty={masterOrder.Quantity}, Limit={masterOrder.LimitPrice}, Stop={masterOrder.StopPrice}, OCO={followerOco}" );
				}
				catch(Exception ex)
				{
					Log( $"[ERROR] Update-resubmit failed for {followerOrder.Account.Name}: {ex.Message}" );
				}
			}

			private void CancelMirroredOrders( string masterKey )
			{
				if(!mirrorMap.TryGetValue( masterKey, out var followers ))
					return;

				foreach(var o in followers)
				{
					try
					{
						o.Account.Cancel( new[] { o } );
						Log( $"[CANCEL] {o.Account.Name}: {o.Instrument.FullName}, {o.OrderAction}, Qty={o.Quantity}" );
					}
					catch(Exception ex)
					{
						Log( $"[ERROR] Cancel failed for {o.Account.Name}: {ex.Message}" );
					}
				}
			}

			private void LogFollowerStates( string masterKey )
			{
				try
				{
					if(mirrorMap.TryGetValue( masterKey, out var followers ))
					{
						var grouped = followers
							.GroupBy( fo => fo.Account.Name )
							.Select( g =>
							{
								var parts = g.Select( fo => string.Format(
									"{0} {1} Qty={2} State={3} Filled={4} Price:{5}",
									fo.Instrument.FullName,
									fo.OrderAction,
									fo.Quantity,
									fo.OrderState,
									fo.Filled,
									fo.AverageFillPrice
								) );
								return g.Key + ": " + string.Join( " | ", parts );
							} );

						foreach(var line in grouped)
							Log( $"[FOLLOWER STATE] {line}" );
					}
				}
				catch(Exception ex)
				{
					Log( $"[ERROR] LogFollowerStates failed: {ex.Message}" );
				}
			}

			private void CheckFinalSync( string masterKey, string masterState, string instrument, OrderAction action, int qty )
			{
				try
				{
					if(mirrorMap.TryGetValue( masterKey, out var followers ))
					{
						bool allMatch = followers.All( f => f.OrderState.ToString() == masterState );
						if(allMatch)
							Log( $"[SYNC COMPLETE] Master {instrument} {action} {qty} and ALL followers are {masterState}." );
						else
							Log( $"[SYNC WARNING] Master {instrument} {action} {qty} is {masterState}, but some followers are not." );
					}
				}
				catch(Exception ex)
				{
					Log( $"[ERROR] CheckFinalSync failed: {ex.Message}" );
				}
			}

			private void CancelAllOrders( Account acct )
			{
				foreach(var o in acct.Orders.Where( o => o.OrderState == OrderState.Working ).ToArray())
				{
					try
					{
						acct.Cancel( new[] { o } );
						Log( $"[KILL SWITCH CANCEL] {acct.Name}: {o.Instrument.FullName} {o.OrderAction} Qty={o.Quantity}" );
					}
					catch(Exception ex)
					{
						Log( $"[ERROR] Cancel failed {acct.Name}: {ex.Message}" );
					}
				}
			}

			private void CustomFlattenAllPositions( Account acct )
			{
				try
				{
					var closed = new List<string>();
					bool flattened = false;

					foreach(var pos in acct.Positions.Where( p => p.Quantity != 0 ).ToArray())
					{
						var action = pos.MarketPosition == MarketPosition.Long
							? OrderAction.Sell
							: OrderAction.Buy;

						var o = acct.CreateOrder(
							pos.Instrument,
							action,
							OrderType.Market,
							OrderEntry.Manual,
							TimeInForce.Day,
							Math.Abs( pos.Quantity ),
							0, 0,
							null,
							"KillSwitchExit",
							DateTime.Now,
							null
						);

						acct.Submit( new[] { o } );
						closed.Add( $"{pos.Instrument.FullName} {pos.MarketPosition} {pos.Quantity}→{action} Market" );
						flattened = true;
					}

					if(flattened)
						Log( $"[KILL SWITCH FLATTEN] {acct.Name}: {string.Join( " | ", closed )}" );
				}
				catch(Exception ex)
				{
					Log( $"[ERROR] Custom flatten failed {acct.Name}: {ex.Message}" );
					if(!acct.Positions.Any( p => p.Quantity != 0 )) return;

					try
					{
						acct.Flatten( acct.Positions.Select( p => p.Instrument ).ToList() );
						Log( $"[KILL SWITCH FLATTEN-FALLBACK] {acct.Name}: Fallback Flatten called." );
					}
					catch(Exception ex2)
					{
						Log( $"[ERROR] Built-in flatten also failed {acct.Name}: {ex2.Message}" );
					}
				}
			}

			/*private void OnOrderUpdate( object sender, OrderEventArgs e )
			{
				if(!IsActive) return;
				try
				{
					foreach(var f in Followers)
						ReplicateOrder( f, e );
				}
				catch(Exception ex)
				{
					ADLog.Write( $"OnOrderUpdate error: {ex.Message}" );
				}
			}*/

			private void OnExecutionUpdate( object sender, ExecutionEventArgs e )
			{
				if(!IsActive) return;
				try
				{
					//foreach(var f in Followers)
						//ReplicateExecution( f, e );
				}
				catch(Exception ex)
				{
					ADLog.Write( $"OnExecutionUpdate error: {ex.Message}" );
				}
			}

			// ---------- Replication stubs ----------

			/*private void ReplicateOrder( Account follower, OrderEventArgs e )
			{
				// TODO: implement your order copy logic
				// Example:
				// 1. Map instrument and side.
				// 2. Adjust quantity by multiplier.
				// 3. Submit via follower.CreateOrder() / follower.Submit().
			}

			private void ReplicateExecution( Account follower, ExecutionEventArgs e )
			{
				// TODO: optional fill/position sync logic.
			}*/

			// ---------- Utilities ----------

			internal void ExecuteFlattenAll()
			{
				try
				{
					if(Master != null) FlattenAccount( Master );
					foreach(var f in Followers)
						if(f != null) FlattenAccount( f );
				}
				catch(Exception ex)
				{
					ADLog.Write( $"Flatten error: {ex.Message}" );
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
					Log( $"FlattenAccount error [{acct.Name}]: {ex.Message}" );
					ADLog.Write( $"FlattenAccount error [{acct.Name}]: {ex.Message}" );
				}
			}


			#region Async Logging

			private void StartLogProcessor()
			{
				if(logRunning) return;
				logRunning = true;

				Task.Run( async () =>
				{
					while(logRunning)
					{
						while(logQueue.TryDequeue( out var message ))
							NinjaTrader.Code.Output.Process( $"[{DateTime.Now:HH:mm:ss}] {message}", PrintTo.OutputTab1 );

						try { await Task.Delay( 10 ); }
						catch
						{ // ignore
						}
					}
				} );
			}

			private void Log( string message )
			{
				if(EnableLogging)
					logQueue.Enqueue( message );
			}

			#endregion


			internal string Summary()
			{
				var master = Master?.Name ?? "None";
				var followers = Followers.Count > 0
					? string.Join( ", ", Followers.Select( f => f.Name ) )
					: "None";
				return $"Master: {master} | Followers: {followers}";
			}
		}

		internal readonly Copier CopierMgr = new();


		internal void Copier_OnExecutionUpdate( Account srcAccount, ExecutionEventArgs e )
		{
		}

		// Called by Core on every OrderUpdate
		// Current starter behavior expected later:
		// - If master exists and submits a Market order, mirror to followers with Multiplier
		// - Extend to limit/stop/OCO in your implementation
		internal void Copier_OnOrderUpdate( Account srcAccount, OrderEventArgs e )
		{
			// No-op stub so you can implement later.
			// Suggested outline:
			//
			Order o = e.Order;

			if( o.OrderState == OrderState.Working)
				Print( $"Place order for master {srcAccount.Name}" );

			//Print( "Copier_OnOrderUpdate: " + o.OrderState + ", " + o.Account.Name );
			var master = Rows.FirstOrDefault( r => r.Role == RowRole.Master )?.Acct;

			if(master == null || srcAccount != master)
			{
				Print( "Can't find master account" );
				return;
			}
			if(e.Order == null || e.Order.OrderState != OrderState.Working)
			{
				//Print( "Not a working order yet...." );
				return;
			}
			//if(e.Order.OrderType != OrderType.Market)
			//{
			//	Print( "Not a market order. out" );
			//	return;
			//}

			if(o.OrderState == OrderState.Working) 
			foreach(var row in Rows.Where( r => r.Role == RowRole.Follower ))
			{
				int qty = Math.Max( 1, e.Order.Quantity * row.Size );
				try
				{
					Print( $"Place order to follower: {row.AccountName}" );
					//var copy = row.Acct.CreateOrder( e.Order.Instrument, e.Order.OrderAction, OrderType.Market, qty, 0, 0, "AD-COPY", "copy" );
					//row.Acct.Submit( new[] { copy } );
				}
				catch(System.Exception ex) { ADLog.Write( $"Copy error [{row.AccountName}]: {ex.Message}" ); }
			}
		}
    }
}
