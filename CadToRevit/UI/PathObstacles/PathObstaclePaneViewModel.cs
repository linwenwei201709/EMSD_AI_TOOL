using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace CadToRevit.UI.PathObstacles
{
    public sealed class PathObstaclePaneViewModel : INotifyPropertyChanged
    {
        private bool _isDrawing;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<PathObstacleItemViewModel> Items { get; } = new ObservableCollection<PathObstacleItemViewModel>();

        public ICommand CreateAreaCommand { get; }

        public ICommand DeleteAllCommand { get; }

        public bool IsDrawing
        {
            get { return _isDrawing; }
            set
            {
                if (Set(ref _isDrawing, value))
                {
                    OnPropertyChanged(nameof(CanEditItems));
                    OnPropertyChanged(nameof(CanDeleteAll));
                }
            }
        }

        public bool CanEditItems
        {
            get { return !IsDrawing; }
        }

        public bool CanDeleteAll
        {
            get { return CanEditItems && Items.Count > 0; }
        }

        public bool HasNoItems
        {
            get { return Items.Count == 0; }
        }

        public PathObstaclePaneViewModel()
        {
            CreateAreaCommand = new RelayCommand(_ => PathObstacleRuntime.RequestBeginDrawing());
            DeleteAllCommand = new RelayCommand(_ => DeleteAll());
        }

        internal void SetRecords(IEnumerable<PathObstacleRecord> records)
        {
            Action update = delegate
            {
                Items.Clear();
                if (records != null)
                {
                    foreach (PathObstacleRecord record in records)
                    {
                        if (record == null)
                        {
                            continue;
                        }

                        Items.Add(new PathObstacleItemViewModel(record));
                    }
                }

                OnPropertyChanged(nameof(HasNoItems));
                OnPropertyChanged(nameof(CanDeleteAll));
            };

            Application application = Application.Current;
            if (application != null && application.Dispatcher != null && !application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.BeginInvoke(update);
                return;
            }

            update();
        }

        private void DeleteAll()
        {
            int count = Items.Count;
            if (count <= 0)
            {
                return;
            }

            if (PathObstacleConfirmWindow.Confirm(
                "Clear All Restricted Areas?",
                "You are about to permanently delete all " + count + " restricted areas.\nThis action cannot be undone."))
            {
                PathObstacleRuntime.RequestDeleteAll();
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

    public sealed class PathObstacleItemViewModel
    {
        private readonly PathObstacleRecord _record;

        public string ObstacleId { get; }

        public string Name { get; }

        public int ElementId { get; }

        public ICommand LocateCommand { get; }

        public ICommand EditCommand { get; }

        public ICommand DeleteCommand { get; }

        public PathObstacleItemViewModel(PathObstacleRecord record)
        {
            _record = record;
            ObstacleId = record.ObstacleId ?? string.Empty;
            Name = string.IsNullOrWhiteSpace(record.Name) ? "Restricted Area" : record.Name.Trim();
            ElementId = record.ElementIdValue;
            LocateCommand = new ItemCommand(_ => Locate());
            EditCommand = new ItemCommand(_ => EditName());
            DeleteCommand = new ItemCommand(_ => Delete());
        }

        private void Locate()
        {
            PathObstacleRuntime.RequestLocate(_record);
        }

        private void EditName()
        {
            PathObstacleEditNameWindow window = new PathObstacleEditNameWindow(Name);
            bool? result = window.ShowDialog();
            if (result == true)
            {
                PathObstacleRuntime.RequestRename(_record, window.EditedName);
            }
        }

        private void Delete()
        {
            if (PathObstacleConfirmWindow.Confirm(
                "Delete Restricted Area?",
                "Are you sure you want to delete? It will no longer block\nlayouts or routing."))
            {
                PathObstacleRuntime.RequestDelete(_record);
            }
        }

        private sealed class ItemCommand : ICommand
        {
            private readonly Action<object> _execute;

            public ItemCommand(Action<object> execute)
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
