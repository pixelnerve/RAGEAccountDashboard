#region using
using NinjaTrader.Cbi;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

#endregion

namespace NinjaTrader.AddOns
{
    public enum RowRole { None, Master, Follower }

    // Simple logger
    internal static class ADLog
    {
        private static readonly object _lock = new object();
        public static string LogFilePath { get; set; }
        public static void Write(string msg)
        {
            try
            {
                lock (_lock)
                    System.IO.File.AppendAllText(LogFilePath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
            }
            catch { /* ignore */ }
        }
    }

    // Brushes and colors in one place
    internal static class ADTheme
    {
        public static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        public static readonly Brush RowBg = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21));
        public static readonly Brush AltRowBg = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25));
        public static readonly Brush Fore = Brushes.Gainsboro;

        public static readonly Brush PnLPos = new SolidColorBrush(Color.FromRgb(0x2E, 0xC4, 0x44));
        public static readonly Brush PnLNeg = new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E));

        public static readonly Brush PosLong = new SolidColorBrush(Color.FromArgb(0x60, 0x33, 0xFF, 0x33));
        public static readonly Brush PosShort = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x33, 0x33));

        public static readonly Brush RowMaster = new SolidColorBrush(Color.FromArgb(255, 64, 0, 0));
        public static readonly Brush RowFollower = new SolidColorBrush(Color.FromArgb(255, 64, 64, 32));

		public static readonly Brush White = new SolidColorBrush( Color.FromRgb( 255, 255, 255 ) );

		public static readonly Brush TextPosFlat = new SolidColorBrush( Color.FromArgb( 128, 255, 255, 255 ) );

		public static readonly Color ConnectRow = Colors.DarkOliveGreen;
		public static readonly Color DisconnectRow = Colors.DarkRed;
	}

	internal static class ADUI
	{
		public static readonly double ColumnPaddingLeft = 6;
	}

	// Relay
	public sealed class RelayCommand : ICommand
    {
        private readonly Action _run; private readonly Func<bool> _can;
        public RelayCommand(Action run, Func<bool> can = null) { _run = run; _can = can; }
        public bool CanExecute(object p) => _can == null || _can();
        public void Execute(object p) => _run();
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }

    // Converters
    public sealed class PnLBrushConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is double d) return d > 0 ? ADTheme.PnLPos : d < 0 ? ADTheme.PnLNeg : ADTheme.Fore;
            return ADTheme.Fore;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => null;
    }


	public sealed class PosBackgroundConverter : IValueConverter
	{
		public object Convert( object v, Type t, object p, CultureInfo c )
		{
			if(v is MarketPosition mp)
				return mp == MarketPosition.Long ? ADTheme.PosLong :
					   mp == MarketPosition.Short ? ADTheme.PosShort :
					   Brushes.Transparent;
			return Brushes.Transparent;
		}
		public object ConvertBack( object v, Type t, object p, CultureInfo c ) => null;
	}

	public sealed class PosTextConverter : IValueConverter
	{
		public object Convert( object v, Type t, object p, CultureInfo c )
		{
			if(v is MarketPosition mp)
				return mp == MarketPosition.Long || mp == MarketPosition.Short ? ADTheme.White : ADTheme.TextPosFlat;
			return Brushes.Transparent;
		}
		public object ConvertBack( object v, Type t, object p, CultureInfo c ) => null;
	}

	public sealed class RoleToEnabledConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			if(value is RowRole role)
				return role == RowRole.Follower; // Enable only for followers
			return true;
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
			=> throw new NotImplementedException();
	}

	public sealed class RoleToEnabledConverter2 : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			if(value is RowRole role)
				return role != RowRole.Master; 
			return true;
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
			=> throw new NotImplementedException();
	}

	public sealed class RoleRowBackgroundConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is RowRole rr) return rr == RowRole.Master ? ADTheme.RowMaster :
                                        rr == RowRole.Follower ? ADTheme.RowFollower : ADTheme.RowBg;
            return ADTheme.RowBg;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => null;
    }

    public sealed class MultToIndexConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        { int mult = v is int i ? i : 1; return Math.Max(0, Math.Min(9, mult - 1)); } // 1..10 -> 0..9
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        { int idx = v is int i ? i : 0; return Math.Max(1, Math.Min(10, idx + 1)); }  // 0..9 -> 1..10
    }

    // Account row
    public class AccountRow : INotifyPropertyChanged
    {
        public Account Acct { get; }
        public string AccountName => Acct?.Name ?? "";
        public string ConnectionName => GetConnName(Acct);

        public RowRole Role { get => _role; set { _role = value; OnChanged(); } }
        public bool Hidden { get => _hidden; set { _hidden = value; OnChanged(); } }
        public bool RiskEnabled { get => _riskEnabled; set { _riskEnabled = value; OnChanged(); } }

		public MarketPosition Dir
		{
			get => _dir;
			set
			{
				_dir = value;
				OnChanged( nameof( Dir ) ); // always notify, even if same value
				if(_dir == MarketPosition.Flat)
					OnChanged( nameof( Pos ) ); // ensure converter reevaluates
			}
		}
		public int Pos 
		{ 
			get => _pos; 
			set 
			{ 
				_pos = value; OnChanged( nameof(Pos) );
				if(_pos == 0)
					OnChanged( nameof( Dir ) ); // ensure converter reevaluates
			}
		}
        public int Qty { get => _qty; set { _qty = value; OnChanged(); } }
        public int Size { get => _mult; set { _mult = Math.Max(1, value); OnChanged(); } }

        public double Unrealized { get => _unrl; set { _unrl = value; OnChanged(); OnChanged(nameof(TotalPnL)); } }
        public double Realized { get => _rlz; set { _rlz = value; OnChanged(); OnChanged(nameof(TotalPnL)); } }
        public double Commissions { get => _comm; set { _comm = value; OnChanged(); } }
		public double TrailingMaxDrawdown { get => _maxdd; set { _maxdd = value; OnChanged(); } }
		public double CashValue { get => _cash; set { _cash = value; OnChanged(); } }
        public double NetLiq { get => _net; set { _net = value; OnChanged(); } }

        public double FromFunded => NetLiq - StartBalance;
        public double AutoLiquidate { get => _autoLiq; set { _autoLiq = value; OnChanged(); } }
        public double FromClosed => Realized - DayRealizedStart;
        public double FromLoss => Math.Min(0, TotalPnL);
        public double DailyGoal { get => _goal; set { _goal = value; OnChanged(); } }
        public double DailyLoss { get => _loss; set { _loss = value; OnChanged(); } }
        public double TotalPnL => Unrealized + Realized;

        public double StartBalance { get; set; }
        public double DayRealizedStart { get; set; }

        public ICommand FlattenCmd { get; }
        public ICommand HideCmd { get; }
        public ICommand RoleButtonCmd { get; }

        // noise filters
        public double LastCash; public double LastRealized; public double LastComms; public double LastUnrl; public double LastNet; public double LastTrailingMaxDrawdown;

		int _pos, _qty, _mult = 1;
        double _unrl, _rlz, _cash, _net, _autoLiq, _goal = 400, _loss = -400, _comm, _maxdd;
        bool _hidden, _riskEnabled = false;
        RowRole _role = RowRole.None;
		MarketPosition _dir = MarketPosition.Flat;

		private readonly Func<AccountRow, bool> _canChangeRole;
        private readonly Action<AccountRow> _onRoleClick;

		[Browsable( false )]
		public DataGridRow RowRef { get; set; }

		public AccountRow(Account a, Func<AccountRow, bool> canChangeRole, Action<AccountRow> onRoleClick)
        {
            Acct = a;
            _canChangeRole = canChangeRole; _onRoleClick = onRoleClick;
			// FIXME
			FlattenCmd = new RelayCommand( () => Safe( () => { } ) ); 
			//FlattenCmd = new RelayCommand(() => Safe(() => Acct.FlattenEverything()));
			HideCmd = new RelayCommand(() => Hidden = true);
            RoleButtonCmd = new RelayCommand(() => { if (_canChangeRole(this)) _onRoleClick(this); });
        }

        public void Safe(Action act)
        {
            try { act(); }
            catch (Exception ex) { ADLog.Write($"Error: {ex.Message}"); }
        }

        static string GetConnName(Account a)
        {
            try
            {
                // Defensive. Different adapters expose different names.
                var c = a?.Connection;
                if (c == null) return "Unknown";
                var n = c.Options?.Name ?? c.Options.Name ?? c.ToString();
                return string.IsNullOrWhiteSpace(n) ? "Unknown" : n;
            }
            catch { return "Unknown"; }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        //void OnChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
		protected void OnChanged( [CallerMemberName] string name = null )
		{
			PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( name ) );
		}

	}

	// Connection or total summary row
	public class SummaryRow : INotifyPropertyChanged
    {
        public string Label { get => _label; set { _label = value; OnChanged(); } }
        public int AccountCount { get => _count; set { _count = value; OnChanged(); } }
		//public double TotalSize { get => _size; set { _size = value; OnChanged(); } }
		public double TotalPos { get => _pos; set { _pos = value; OnChanged(); } }
		public double TotalUnrealized { get => _u; set { _u = value; OnChanged(); } }
        public double TotalRealized { get => _r; set { _r = value; OnChanged(); } }
        public double TotalCash { get => _c; set { _c = value; OnChanged(); } }
        public double TotalNetLiq { get => _n; set { _n = value; OnChanged(); } }
        public double TotalComms { get => _m; set { _m = value; OnChanged(); } }
        public double TotalPnL => TotalUnrealized + TotalRealized;

		public MarketPosition Dir
		{
			get => _dir;
			set
			{
				_dir = value;
				OnChanged( nameof( Dir ) ); // always notify, even if same value
			}
		}

		string _label; int _count;
		double _pos, _u, _r, _c, _n, _m; //, _size;
		MarketPosition _dir = MarketPosition.Flat;

		public event PropertyChangedEventHandler PropertyChanged;
        //void OnChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
		protected void OnChanged( [CallerMemberName] string name = null )
		{
			PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( name ) );
		}
	}
}
