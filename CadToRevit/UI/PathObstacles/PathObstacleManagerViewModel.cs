using CadToRevit.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstacleManagerViewModel : INotifyPropertyChanged
    {
        private bool _hasNoRecords = true;

        public ObservableCollection<PathObstacleRecord> Records { get; } = new ObservableCollection<PathObstacleRecord>();

        public bool HasNoRecords
        {
            get { return _hasNoRecords; }
            private set
            {
                if (_hasNoRecords != value)
                {
                    _hasNoRecords = value;
                    OnPropertyChanged();
                }
            }
        }

        public void SetRecords(System.Collections.Generic.IEnumerable<PathObstacleRecord> records)
        {
            Records.Clear();
            if (records != null)
            {
                foreach (PathObstacleRecord record in records)
                {
                    Records.Add(record);
                }
            }

            HasNoRecords = Records.Count == 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
