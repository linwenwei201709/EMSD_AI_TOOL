using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomListPaneViewModel : INotifyPropertyChanged
    {
        private bool _suppressSelectionChanged;
        private string _headerTitle = "Room Management";
        private string _summaryText = string.Empty;
        private RoomListItemViewModel _selectedRoom;
        private LiftListItemViewModel _selectedLift;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<RoomListItemViewModel> Rooms { get; } = new ObservableCollection<RoomListItemViewModel>();

        public ObservableCollection<LiftListItemViewModel> Lifts { get; } = new ObservableCollection<LiftListItemViewModel>();

        public ICommand RefreshCommand { get; }

        public ICommand AutoDetectRoomsCommand { get; }

        public ICommand AutoDetectLiftsCommand { get; }

        public ICommand CreateRoomCommand { get; }

        public ICommand CreateLiftCommand { get; }

        public string HeaderTitle
        {
            get { return _headerTitle; }
            set { Set(ref _headerTitle, value); }
        }

        public string SummaryText
        {
            get { return _summaryText; }
            set { Set(ref _summaryText, value); }
        }

        public RoomListItemViewModel SelectedRoom
        {
            get { return _selectedRoom; }
            set
            {
                if (Set(ref _selectedRoom, value) && !_suppressSelectionChanged && value != null)
                {
                    RoomRecognitionPaneRuntime.OnListRoomSelected(value);
                }
            }
        }

        public LiftListItemViewModel SelectedLift
        {
            get { return _selectedLift; }
            set
            {
                if (Set(ref _selectedLift, value) && !_suppressSelectionChanged && value != null)
                {
                    RoomRecognitionPaneRuntime.OnListLiftSelected(value);
                }
            }
        }

        public RoomListPaneViewModel()
        {
            RefreshCommand = new RelayCommand(_ => RoomRecognitionPaneRuntime.RefreshSelectionState());
            AutoDetectRoomsCommand = new RelayCommand(async _ => await RoomRecognitionPaneRuntime.RequestAutoDetectRoomsAsync());
            AutoDetectLiftsCommand = new RelayCommand(async _ => await RoomRecognitionPaneRuntime.RequestAutoDetectLiftsAsync());
            CreateRoomCommand = new RelayCommand(async _ => await RoomRecognitionPaneRuntime.RequestCreateManualRoomAsync());
            CreateLiftCommand = new RelayCommand(async _ => await RoomRecognitionPaneRuntime.RequestCreateManualLiftAsync());
        }

        internal void SetSelectedRoomSilently(RoomListItemViewModel item)
        {
            _suppressSelectionChanged = true;
            try
            {
                SelectedRoom = item;
            }
            finally
            {
                _suppressSelectionChanged = false;
            }
        }

        internal void SetSelectedLiftSilently(LiftListItemViewModel item)
        {
            _suppressSelectionChanged = true;
            try
            {
                SelectedLift = item;
            }
            finally
            {
                _suppressSelectionChanged = false;
            }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;

            public RelayCommand(Action<object> execute)
            {
                _execute = execute;
            }

            public bool CanExecute(object parameter)
            {
                return _execute != null;
            }

            public void Execute(object parameter)
            {
                _execute?.Invoke(parameter);
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }
        }
    }
}
