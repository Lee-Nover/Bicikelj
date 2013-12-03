﻿using Caliburn.Micro;
using Microsoft.Phone.Controls;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using Bicikelj.Model;
using System.Collections.Generic;
using Microsoft.Phone.Scheduler;
using Bicikelj.Views;

namespace Bicikelj.ViewModels
{
    public class RentTimerViewModel : Screen
    {
        readonly IEventAggregator events;
        private Reminder reminder;
        private DateTime? rentStarted = null;
        private IDisposable dispTimer;

        public bool CountingDown { get { return dispTimer != null; } }
        public KeyValuePair<int, string>[] ReminderTimes { get; private set; }

        private KeyValuePair<int, string> selectedReminderTime;
        public KeyValuePair<int, string> SelectedReminderTime
        {
            get { return selectedReminderTime; }
            set {
                selectedReminderTime = value;
                NotifyOfPropertyChange(() => SelectedReminderTime);
                NotifyOfPropertyChange(() => ReminderTimeMinutes);
            }
        }

        public int ReminderTimeMinutes { 
            get { return selectedReminderTime.Key; }
            set
            {
                var rt = (from kv in ReminderTimes where kv.Key == value select kv).FirstOrDefault();
                if (string.IsNullOrEmpty(rt.Value))
                    rt = ReminderTimes[0];
                SelectedReminderTime = rt;
            }
        }

        public TimeSpan RentTime { get; set; }

        private TimeSpan remainingRentTime;
        public TimeSpan RemainingRentTime { 
            get { return remainingRentTime; }
            set {
                if (value == remainingRentTime) return;
                remainingRentTime = value;
                NotifyOfPropertyChange(() => RemainingRentTime);
                NotifyOfPropertyChange(() => RemainingRentTimeText);
            }
        }

        public string RemainingRentTimeText { get { return RemainingRentTime.ToString(@"hh\:mm\:ss"); } }

        public DateTime RentStarted { 
            get { return rentStarted.HasValue ? rentStarted.Value : DateTime.MinValue; }
            set { rentStarted = value; }
        }

        public string ToggleTimerText { get; set; }

        public RentTimerViewModel(IEventAggregator events)
        {
            this.events = events;
            RentTime = TimeSpan.FromMinutes(30);
            ReminderTimes = new KeyValuePair<int, string>[] {
                new KeyValuePair<int, string>(0, "no reminder"),
    #if DEBUG
                new KeyValuePair<int, string>(1, "1 minute"),
                new KeyValuePair<int, string>(3, "3 minutes"),
    #endif
                new KeyValuePair<int, string>(5, "5 minutes"),
                new KeyValuePair<int, string>(10, "10 minutes"),
                new KeyValuePair<int, string>(15, "15 minutes"),
                new KeyValuePair<int, string>(20, "20 minutes")//,
                //new KeyValuePair<int, string>(30, "30 minutes")
            };
            selectedReminderTime = ReminderTimes[0];
            ToggleTimerText = "start the timer";
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            if (rentStarted.HasValue && !CountingDown)
                StartTimer();
        }

        private RentTimerView rtView = null;
        protected override void OnViewAttached(object view, object context)
        {
            base.OnViewAttached(view, context);
            rtView = view as RentTimerView;
        }

        public void StartTimer()
        {
            if (dispTimer != null)
                StopTimer();
            rentStarted = DateTime.Now;
            RemainingRentTime = RentTime;
            UpdateControls(false);
            ToggleTimerText = "stop the timer";
            NotifyOfPropertyChange(() => ToggleTimerText);
            dispTimer = Observable.Interval(TimeSpan.FromSeconds(1))
                .SubscribeOn(ThreadPoolScheduler.Instance)
                .TakeWhile(_ => RemainingRentTime > TimeSpan.Zero)
                .ObserveOn(ReactiveExtensions.SyncScheduler)
                .Finally(StopTimer)
                .Subscribe(UpdateRemainingTime);

            NotifyOfPropertyChange(() => CountingDown);
            if (ReminderTimeMinutes > 0)
                CreateReminder(rentStarted.Value + RentTime, TimeSpan.FromMinutes(ReminderTimeMinutes));
        }

        public void StopTimer()
        {
            RemoveReminder();
            UpdateControls(true);
            ToggleTimerText = "start the timer";
            NotifyOfPropertyChange(() => ToggleTimerText);
            if (dispTimer == null) return;
            dispTimer.Dispose();
            dispTimer = null;
            RemainingRentTime = RentTime;
            NotifyOfPropertyChange(() => CountingDown);
        }

        public void ToggleTimer()
        {
            if (dispTimer == null)
                StartTimer();
            else
                StopTimer();
        }

        private void UpdateControls(bool enabled)
        {
            if (rtView == null) return;
            rtView.RentTime.IsEnabled = enabled;
            rtView.ReminderTimes.IsEnabled = enabled;
        }

        private void UpdateRemainingTime(long iteration = 0)
        {
            RemainingRentTime = rentStarted.HasValue ? RentTime - (DateTime.Now - rentStarted.Value) : TimeSpan.Zero;
        }

        private void CreateReminder(DateTime dueTime, TimeSpan remindBefore)
        {
            RemoveReminder();
            if (dueTime < DateTime.Now) return;
            reminder = new Reminder("PublicBikesExpirationReminder");
            reminder.Content = "your free ride will end soon. find a dock nearby to avoid extra charges";
            reminder.BeginTime = dueTime - remindBefore;
            reminder.ExpirationTime = dueTime;
            reminder.Title = "public bikes ride reminder";
            reminder.RecurrenceType = RecurrenceInterval.None;
            var navService = IoC.Get<INavigationService>();
            var vmUri = navService.UriFor<RentTimerViewModel>()
                .WithParam<DateTime>(vm => vm.RentStarted, RentStarted)
                .WithParam<TimeSpan>(vm => vm.RentTime, RentTime)
                .WithParam<int>(vm => vm.ReminderTimeMinutes, ReminderTimeMinutes)
                .BuildUri();
            reminder.NavigationUri = new Uri("/Main.xaml?redirect=RentTimerViewModel&" + vmUri.Query);
            ScheduledActionService.Add(reminder);
        }

        private static void RemoveReminder()
        {
            var r = ScheduledActionService.Find("PublicBikesExpirationReminder");
            if (r != null)
                ScheduledActionService.Remove("PublicBikesExpirationReminder");
        }
    }
}
