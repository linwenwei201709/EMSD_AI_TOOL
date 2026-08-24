using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Windows.Input;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomListItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Key { get; set; }

        public string StableRoomKey { get; set; }

        public bool IsProbeRoomCard { get; set; }

        public string Title { get; set; }

        public string Subtitle { get; set; }

        public string AreaText { get; set; }

        public string AreaLine { get; set; }

        public string LevelText { get; set; }

        public string LevelLine { get; set; }

        public string StatusText { get; set; }

        public string StatusLine { get; set; }

        public string TargetType { get; set; }

        public string RoomLengthLine { get; set; }

        public string RoomWidthLine { get; set; }

        public string RoomHeightLine { get; set; }

        public string DoorWidthLine { get; set; }

        public string DoorHeightLine { get; set; }

        public string AvailableUsableAreaLine { get; set; }

        public string RoomSizeLine { get; set; }

        public string DoorSizeLine { get; set; }

        public string AreaSummaryLine { get; set; }

        public ICommand EditCommand { get; set; } = new NoOpCommand();

        public ICommand DeleteCommand { get; set; } = new NoOpCommand();

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void RaiseTitleChanged()
        {
            OnPropertyChanged(nameof(Title));
        }

        private sealed class NoOpCommand : ICommand
        {
            public bool CanExecute(object parameter)
            {
                return true;
            }

            public void Execute(object parameter)
            {
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }
        }
    }
}
