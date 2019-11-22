using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using NBitcoin;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using WalletWasabi.Gui.Models;
using WalletWasabi.Gui.ViewModels;
using WalletWasabi.Logging;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;

namespace WalletWasabi.Gui.Controls.WalletExplorer
{
	public class CoinListViewModel : ViewModelBase
	{
		private static HashSet<SmartCoinStatus> NotVisibleStatuses = new HashSet<SmartCoinStatus>()
			{
				SmartCoinStatus.Confirmed,
				SmartCoinStatus.Unconfirmed
			};

		private CompositeDisposable Disposables { get; set; }

		public SourceList<CoinViewModel> RootList { get; private set; }

		private ReadOnlyObservableCollection<CoinViewModel> _coinViewModels;
		private SortExpressionComparer<CoinViewModel> _myComparer;

		private CoinViewModel _selectedCoin;
		private bool? _selectAllCheckBoxState;
		private SortOrder _statusSortDirection;
		private SortOrder _privacySortDirection;
		private SortOrder _amountSortDirection;
		private bool? _selectPrivateCheckBoxState;
		private bool? _selectNonPrivateCheckBoxState;
		private GridLength _coinJoinStatusWidth;
		private SortOrder _clustersSortDirection;
		private Money _selectedAmount;
		private bool _isAnyCoinSelected;
		private bool _labelExposeCommonOwnershipWarning;
		private bool _selectAllNonPrivateVisible;
		private bool _selectAllPrivateVisible;
		private ShieldState _selectAllPrivateShieldState;
		private ShieldState _selectAllNonPrivateShieldState;
		private bool _isCoinListLoading;

		public Global Global { get; }
		public CoinListContainerType CoinListContainerType { get; }
		public ReactiveCommand<Unit, Unit> EnqueueCoin { get; }
		public ReactiveCommand<Unit, Unit> DequeueCoin { get; }
		public ReactiveCommand<Unit, Unit> SelectAllCheckBoxCommand { get; }
		public ReactiveCommand<Unit, Unit> SelectPrivateCheckBoxCommand { get; }
		public ReactiveCommand<Unit, Unit> SelectNonPrivateCheckBoxCommand { get; }
		public ReactiveCommand<Unit, Unit> SortCommand { get; }
		public ReactiveCommand<Unit, Unit> InitList { get; }
		public ReactiveCommand<Unit, Unit> RefreshCoinList { get; }

		public event EventHandler DequeueCoinsPressed;

		public event EventHandler<CoinViewModel> SelectionChanged;

		public event EventHandler CoinListChanged;

		public event EventHandler<CoinViewModel> CoinStatusChanged;

		public event EventHandler CoinListShown;

		public ReadOnlyObservableCollection<CoinViewModel> Coins => _coinViewModels;

		private SortExpressionComparer<CoinViewModel> MyComparer
		{
			get => _myComparer;
			set => this.RaiseAndSetIfChanged(ref _myComparer, value);
		}

		public CoinViewModel SelectedCoin
		{
			get => _selectedCoin;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedCoin, value);
				this.RaisePropertyChanged(nameof(CanDeqeue));
			}
		}

		public bool CanDeqeue => SelectedCoin?.CoinJoinInProgress ?? false;

		public bool? SelectAllCheckBoxState
		{
			get => _selectAllCheckBoxState;
			set => this.RaiseAndSetIfChanged(ref _selectAllCheckBoxState, value);
		}

		public bool? SelectPrivateCheckBoxState
		{
			get => _selectPrivateCheckBoxState;
			set => this.RaiseAndSetIfChanged(ref _selectPrivateCheckBoxState, value);
		}

		public SortOrder StatusSortDirection
		{
			get => _statusSortDirection;
			set => this.RaiseAndSetIfChanged(ref _statusSortDirection, value);
		}

		public SortOrder AmountSortDirection
		{
			get => _amountSortDirection;
			set => this.RaiseAndSetIfChanged(ref _amountSortDirection, value);
		}

		public SortOrder PrivacySortDirection
		{
			get => _privacySortDirection;
			set => this.RaiseAndSetIfChanged(ref _privacySortDirection, value);
		}

		public SortOrder ClustersSortDirection
		{
			get => _clustersSortDirection;
			set => this.RaiseAndSetIfChanged(ref _clustersSortDirection, value);
		}

		public Money SelectedAmount
		{
			get => _selectedAmount;
			set => this.RaiseAndSetIfChanged(ref _selectedAmount, value);
		}

		public bool IsAnyCoinSelected
		{
			get => _isAnyCoinSelected;
			set => this.RaiseAndSetIfChanged(ref _isAnyCoinSelected, value);
		}

		public bool LabelExposeCommonOwnershipWarning
		{
			get => _labelExposeCommonOwnershipWarning;
			set => this.RaiseAndSetIfChanged(backingField: ref _labelExposeCommonOwnershipWarning, value);
		}

		private void RefreshOrdering()
		{
			var sortExpression = new SortExpressionComparer<CoinViewModel>();
			if (AmountSortDirection != SortOrder.None)
			{
				MyComparer = AmountSortDirection == SortOrder.Increasing
					? sortExpression.ThenByAscending(cvm => cvm.Amount)
					: sortExpression.ThenByDescending(cvm => cvm.Amount);
			}
			else if (PrivacySortDirection != SortOrder.None)
			{
				MyComparer = PrivacySortDirection == SortOrder.Increasing
					? sortExpression.ThenByAscending(cvm => cvm.AnonymitySet)
					: sortExpression.ThenByDescending(cvm => cvm.AnonymitySet);
			}
			else if (ClustersSortDirection != SortOrder.None)
			{
				MyComparer = ClustersSortDirection == SortOrder.Increasing
					? sortExpression.ThenByAscending(cvm => cvm.Clusters)
					: sortExpression.ThenByDescending(cvm => cvm.Clusters);
			}
			else if (StatusSortDirection != SortOrder.None)
			{
				MyComparer = StatusSortDirection == SortOrder.Increasing
					? sortExpression.ThenByAscending(cvm => cvm.Status)
					: sortExpression.ThenByDescending(cvm => cvm.Status);
			}
		}

		public bool? SelectNonPrivateCheckBoxState
		{
			get => _selectNonPrivateCheckBoxState;
			set => this.RaiseAndSetIfChanged(ref _selectNonPrivateCheckBoxState, value);
		}

		public GridLength CoinJoinStatusWidth
		{
			get => _coinJoinStatusWidth;
			set => this.RaiseAndSetIfChanged(ref _coinJoinStatusWidth, value);
		}

		public bool SelectAllNonPrivateVisible
		{
			get => _selectAllNonPrivateVisible;
			set => this.RaiseAndSetIfChanged(ref _selectAllNonPrivateVisible, value);
		}

		public bool SelectAllPrivateVisible
		{
			get => _selectAllPrivateVisible;
			set => this.RaiseAndSetIfChanged(ref _selectAllPrivateVisible, value);
		}

		public ShieldState SelectAllPrivateShieldState
		{
			get => _selectAllPrivateShieldState;
			set => this.RaiseAndSetIfChanged(ref _selectAllPrivateShieldState, value);
		}

		public ShieldState SelectAllNonPrivateShieldState
		{
			get => _selectAllNonPrivateShieldState;
			set => this.RaiseAndSetIfChanged(ref _selectAllNonPrivateShieldState, value);
		}

		public bool IsCoinListLoading
		{
			get => _isCoinListLoading;
			set => this.RaiseAndSetIfChanged(ref _isCoinListLoading, value);
		}

		private bool? GetCheckBoxesSelectedState(Func<CoinViewModel, bool> coinFilterPredicate)
		{
			var coins = Coins.Where(coinFilterPredicate).ToArray();

			bool isAllSelected = coins.All(coin => coin.IsSelected);
			bool isAllDeselected = coins.All(coin => !coin.IsSelected);

			if (isAllDeselected)
			{
				return false;
			}

			if (isAllSelected)
			{
				if (coins.Length != Coins.Count(coin => coin.IsSelected))
				{
					return null;
				}
				return true;
			}

			return null;
		}

		private void SelectAllCoins(bool valueOfSelected, Func<CoinViewModel, bool> coinFilterPredicate)
		{
			var coins = Coins.Where(coinFilterPredicate).ToArray();
			foreach (var c in coins)
			{
				c.IsSelected = valueOfSelected;
			}
		}

		public CoinListViewModel(Global global, CoinListContainerType coinListContainerType)
		{
			Global = global;
			CoinListContainerType = coinListContainerType;
			AmountSortDirection = SortOrder.Decreasing;

			CoinJoinStatusWidth = new GridLength(0);

			RefreshOrdering();

			// Otherwise they're all selected as null on load.
			SelectAllCheckBoxState = false;
			SelectPrivateCheckBoxState = false;
			SelectNonPrivateCheckBoxState = false;

			var sortChanged = this
				.WhenAnyValue(x => x.MyComparer)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Select(_ => MyComparer);

			RootList = new SourceList<CoinViewModel>();
			RootList
				.Connect()
				.Sort(MyComparer, comparerChanged: sortChanged, resetThreshold: 5)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Bind(out _coinViewModels)
				// .OnItemAdded() Do not use this, brings instability on OSX
				// .OnItemRemoved() Do not use this, brings instability on OSX
				.Subscribe();

			SortCommand = ReactiveCommand.Create(RefreshOrdering);

			this.WhenAnyValue(x => x.AmountSortDirection)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x =>
				{
					if (x != SortOrder.None)
					{
						PrivacySortDirection = SortOrder.None;
						StatusSortDirection = SortOrder.None;
						ClustersSortDirection = SortOrder.None;
					}
				});

			this.WhenAnyValue(x => x.ClustersSortDirection)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x =>
				{
					if (x != SortOrder.None)
					{
						AmountSortDirection = SortOrder.None;
						StatusSortDirection = SortOrder.None;
						PrivacySortDirection = SortOrder.None;
					}
				});

			this.WhenAnyValue(x => x.StatusSortDirection)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x =>
				{
					if (x != SortOrder.None)
					{
						AmountSortDirection = SortOrder.None;
						PrivacySortDirection = SortOrder.None;
						ClustersSortDirection = SortOrder.None;
					}
				});

			this.WhenAnyValue(x => x.PrivacySortDirection)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x =>
				{
					if (x != SortOrder.None)
					{
						AmountSortDirection = SortOrder.None;
						StatusSortDirection = SortOrder.None;
						ClustersSortDirection = SortOrder.None;
					}
				});

			DequeueCoin = ReactiveCommand.Create(() =>
				{
					if (SelectedCoin is null)
					{
						return;
					}

					DequeueCoinsPressed?.Invoke(this, EventArgs.Empty);
				},
				this.WhenAnyValue(x => x.CanDeqeue));

			SelectAllCheckBoxCommand = ReactiveCommand.Create(() =>
				{
					switch (SelectAllCheckBoxState)
					{
						case true:
							SelectAllCoins(true, x => true);
							break;

						case false:
							SelectAllCoins(false, x => true);
							break;

						case null:
							SelectAllCoins(false, x => true);
							SelectAllCheckBoxState = false;
							break;
					}
				});

			SelectPrivateCheckBoxCommand = ReactiveCommand.Create(() =>
				{
					switch (SelectPrivateCheckBoxState)
					{
						case true:
							SelectAllCoins(true, x => x.AnonymitySet >= Global.Config.MixUntilAnonymitySet);
							break;

						case false:
							SelectAllCoins(false, x => x.AnonymitySet >= Global.Config.MixUntilAnonymitySet);
							break;

						case null:
							SelectAllCoins(false, x => x.AnonymitySet >= Global.Config.MixUntilAnonymitySet);
							SelectPrivateCheckBoxState = false;
							break;
					}
				});

			SelectNonPrivateCheckBoxCommand = ReactiveCommand.Create(() =>
				{
					switch (SelectNonPrivateCheckBoxState)
					{
						case true:
							SelectAllCoins(true, x => x.AnonymitySet < Global.Config.MixUntilAnonymitySet);
							break;

						case false:
							SelectAllCoins(false, x => x.AnonymitySet < Global.Config.MixUntilAnonymitySet);
							break;

						case null:
							SelectAllCoins(false, x => x.AnonymitySet < Global.Config.MixUntilAnonymitySet);
							SelectNonPrivateCheckBoxState = false;
							break;
					}
				});

			InitList = ReactiveCommand.CreateFromTask(async () =>
			{
				IsCoinListLoading = true;
				await Task.Delay(800);
				CoinListShown?.Invoke(this, null);
			});

			Observable
				.Merge(InitList.ThrownExceptions)
				.Merge(SelectNonPrivateCheckBoxCommand.ThrownExceptions)
				.Merge(SelectPrivateCheckBoxCommand.ThrownExceptions)
				.Merge(SelectAllCheckBoxCommand.ThrownExceptions)
				.Merge(DequeueCoin.ThrownExceptions)
				.Merge(SortCommand.ThrownExceptions)
				.Subscribe(ex => Logger.LogError(ex));

			OnOpen();
		}

		private void RefreshSelectionCheckBoxes()
		{
			SelectAllCheckBoxState = GetCheckBoxesSelectedState(x => true);
			SelectPrivateCheckBoxState = GetCheckBoxesSelectedState(x => x.AnonymitySet >= Global.Config.MixUntilAnonymitySet);
			SelectNonPrivateCheckBoxState = GetCheckBoxesSelectedState(x => x.AnonymitySet < Global.Config.MixUntilAnonymitySet);
		}

		private void RefreshStatusColumnWidth()
		{
			CoinJoinStatusWidth = Coins.Any() && Coins.All(x => NotVisibleStatuses.Contains(x.Status))
										 ? new GridLength(0)
										 : new GridLength(180);
		}

		private void OnOpen()
		{
			Disposables = Disposables is null ?
				new CompositeDisposable() :
				throw new NotSupportedException($"Cannot open {GetType().Name} before closing it.");

			Global.UiConfig
				.WhenAnyValue(x => x.LurkingWifeMode)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedAmount)))
				.DisposeWith(Disposables);

			this.WhenAnyValue(x => x.SelectedAmount)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x => IsAnyCoinSelected = x is null ? false : x > Money.Zero);

			Observable
				.Merge(Observable.FromEventPattern<ReplaceTransactionReceivedEventArgs>(Global.WalletService.TransactionProcessor, nameof(Global.WalletService.TransactionProcessor.ReplaceTransactionReceived)).Select(_ => Unit.Default))
				.Merge(Observable.FromEventPattern<DoubleSpendReceivedEventArgs>(Global.WalletService.TransactionProcessor, nameof(Global.WalletService.TransactionProcessor.DoubleSpendReceived)).Select(_ => Unit.Default))
				.Merge(Observable.FromEventPattern<SmartCoin>(Global.WalletService.TransactionProcessor, nameof(Global.WalletService.TransactionProcessor.CoinSpent)).Select(_ => Unit.Default))
				.Merge(Observable.FromEventPattern<SmartCoin>(Global.WalletService.TransactionProcessor, nameof(Global.WalletService.TransactionProcessor.CoinReceived)).Select(_ => Unit.Default))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(args =>
				{
					try
					{
						OnCoinListChanged();
					}
					catch (Exception ex)
					{
						Logger.LogError(ex);
					}
				})
				.DisposeWith(Disposables);

			Observable
				.Merge(Observable.FromEventPattern(this, nameof(CoinListChanged)).Select(_ => Unit.Default))
				.Throttle(TimeSpan.FromSeconds(2)) // Throttle TransactionProcessor events adds/removes.
				.Merge(Observable.FromEventPattern(this, nameof(CoinListShown)).Select(_ => Unit.Default)) // Load the list immediately.
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(args =>
				{
					try
					{
						var actual = Global.WalletService.TransactionProcessor.Coins.ToHashSet();
						var old = RootList.Items.ToDictionary(c => c.Model, c => c);

						var coinToRemove = old.Where(c => !actual.Contains(c.Key)).ToArray();
						var coinToAdd = actual.Where(c => !old.ContainsKey(c)).ToArray();

						foreach (var item in coinToRemove)
						{
							item.Value.Dispose();
						}
						RootList.RemoveMany(coinToRemove.Select(kp => kp.Value));
						RootList.AddRange(coinToAdd.Select(c => new CoinViewModel(this, c)));

						RefreshSelectionCheckBoxes();
						RefreshStatusColumnWidth();
					}
					catch (Exception ex)
					{
						Logger.LogError(ex);
					}
					finally
					{
						IsCoinListLoading = false;
					}
				})
				.DisposeWith(Disposables);

			Observable.FromEventPattern<CoinViewModel>(this, nameof(CoinStatusChanged))
				.Throttle(TimeSpan.FromSeconds(1))
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(args =>
				{
					try
					{
						RefreshStatusColumnWidth();
					}
					catch (Exception ex)
					{
						Logger.LogError(ex);
					}
				})
				.DisposeWith(Disposables);

			Global.Config
				.WhenAnyValue(x => x.MixUntilAnonymitySet)
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(x =>
				{
					try
					{
						RefreshSelectCheckBoxesShields(x);
					}
					catch (Exception ex)
					{
						Logger.LogError(ex);
					}
				})
				.DisposeWith(Disposables);
		}

		private void RefreshSelectCheckBoxesShields(int mixUntilAnonymitySet)
		{
			var isCriticalPrivate = false;
			var isSomePrivate = mixUntilAnonymitySet <= Global.Config.PrivacyLevelSome;
			var isFinePrivate = mixUntilAnonymitySet <= Global.Config.PrivacyLevelFine;
			var isStrongPrivate = mixUntilAnonymitySet <= Global.Config.PrivacyLevelStrong;

			SelectAllNonPrivateShieldState = new ShieldState(
					!isCriticalPrivate,
					!isSomePrivate,
					!isFinePrivate,
					!isStrongPrivate
					);

			SelectAllPrivateShieldState = new ShieldState(
					isCriticalPrivate,
					isSomePrivate,
					isFinePrivate,
					isStrongPrivate
					);

			RefreshSelectionCheckBoxes();
		}

		public void OnClose()
		{
			foreach (var cvm in RootList.Items)
			{
				cvm.Dispose();
			}

			RootList.Clear(); // This must be called to trigger the OnItemRemoved for every items in the list.

			// Do not dispose the RootList here. It will be reused next time when you open CoinJoinTab or SendTab.
			Disposables?.Dispose();
			Disposables = null;
		}

		public void OnCoinIsSelectedChanged(CoinViewModel cvm)
		{
			RefreshSelectionCheckBoxes();

			SelectionChanged?.Invoke(this, cvm);
			SelectedAmount = Coins.Where(x => x.IsSelected).Sum(x => x.Amount);
			LabelExposeCommonOwnershipWarning = CoinListContainerType == CoinListContainerType.CoinJoinTabViewModel
				? false
				: Coins.Any(c =>
					c.AnonymitySet == 1 && c.IsSelected && Coins.Any(x =>
						x.AnonymitySet > 1 && x.IsSelected));
		}

		public void OnCoinStatusChanged(CoinViewModel cvm)
		{
			CoinStatusChanged?.Invoke(cvm, null);
		}

		public void OnCoinListChanged()
		{
			IsCoinListLoading = true;
			CoinListChanged?.Invoke(this, null);
		}
	}
}
