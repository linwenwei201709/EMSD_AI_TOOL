using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomCustomFamilyItemViewModel : INotifyPropertyChanged
    {
        private bool _isHighlighted;
        private bool _isMissing;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FamilyKey { get; set; }

        public string DisplayName { get; set; }

        public string FileName { get; set; }

        public string FullPath { get; set; }

        public string Description { get; set; }

        public double AirflowM3s { get; set; }

        public int TotalLengthMm { get; set; }

        public int HeightMm { get; set; }

        public int WidthMm { get; set; }

        public int WeightKg { get; set; }

        public int RequiredMaintenanceSpaceMm { get; set; }

        public string RequiredMaintenanceSpaceSide { get; set; }

        public int MbLengthMm { get; set; }

        public int FilterLengthMm { get; set; }

        public int CoilLengthMm { get; set; }

        public int FanLengthMm { get; set; }

        public int ValveChamberLengthMm { get; set; }

        public int ValveChamberWidthMm { get; set; }

        public int ElChamberLengthMm { get; set; }

        public int ElChamberWidthMm { get; set; }

        public int MaintenanceDoorSideMm { get; set; }

        public int MaintenanceOtherSideMm { get; set; }

        public int MaintenanceFrontBackMm { get; set; }

        public ICommand SetCommand { get; set; }

        public bool IsMissing
        {
            get { return _isMissing; }
            set
            {
                if (_isMissing == value)
                {
                    return;
                }

                _isMissing = value;
                OnPropertyChanged();
            }
        }

        public bool IsHighlighted
        {
            get { return _isHighlighted; }
            set
            {
                if (_isHighlighted == value)
                {
                    return;
                }

                _isHighlighted = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class DelegateCommand : ICommand
    {
        private readonly Action<object> _execute;

        internal DelegateCommand(Action<object> execute)
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
