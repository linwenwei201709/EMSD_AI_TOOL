using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Windows.Input;

namespace CadToRevit.UI.Dockable
{
    public sealed class LiftListItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Key { get; set; }

        public string Title { get; set; }

        public string LiftId { get; set; }

        public string LiftType { get; set; }

        public string Dimension { get; set; }

        public string DoorSize { get; set; }

        public string Capacity { get; set; }

        public string LiftInternalLine { get; set; }

        public string DoorSizeLine { get; set; }

        public string CapacityLine { get; set; }

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
