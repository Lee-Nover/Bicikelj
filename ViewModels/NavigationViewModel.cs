﻿using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Bicikelj.Model;
using Bicikelj.Model.Bing;
using Bicikelj.Views;
using Caliburn.Micro;
using Caliburn.Micro.Contrib.Dialogs;
using Microsoft.Phone.Controls.Maps;
using System.Reactive.Linq;
using System.Reactive.Concurrency;
using System.Reactive.PlatformServices;
using System.Threading;
using System.Diagnostics;

namespace Bicikelj.ViewModels
{
    public class NavigationViewModel : Screen
    {
        readonly IEventAggregator events;
        private SystemConfig config;
        private CityContextViewModel cityContext;

        public NavigationViewModel(IEventAggregator events, SystemConfig config, CityContextViewModel cityContext)
        {
            this.events = events;
            this.cityContext = cityContext;
            this.config = config;
            this.CurrentLocation = new LocationViewModel();
            this.DestinationLocation = new LocationViewModel();
        }

        private double travelDistance = double.NaN;
        private double travelDuration = double.NaN;
        private NavigationView view;

        public LocationViewModel NavigateRequest { get; set; }

        public string DistanceString
        {
            get
            {
                if (double.IsNaN(travelDistance))
                    return "travel distance not available";
                else
                    return string.Format("travel distance {0}", LocationHelper.GetDistanceString(travelDistance, config.UseImperialUnits));
            }
        }

        public string DurationString
        {
            get
            {
                if (double.IsNaN(travelDuration))
                    return "travel duration not available";
                else
                    return string.Format("travel duration {0}", TimeSpan.FromSeconds(travelDuration).ToString());
            }
        }

        private string fromLocation;
        public string FromLocation
        {
            get { return fromLocation; }
            set
            {
                if (value == fromLocation)
                    return;
                fromLocation = value;
                NotifyOfPropertyChange(() => FromLocation);
            }
        }

        private string toLocation;
        public string ToLocation {
            get { return toLocation; }
            set
            {
                if (value == toLocation)
                    return;
                toLocation = value;
                NotifyOfPropertyChange(() => ToLocation);
            }
        }

        public string Address {
            get { return DestinationLocation != null ? DestinationLocation.Address : ""; }
            set {
                if (DestinationLocation != null)
                {
                    DestinationLocation.Address = value;
                    NotifyOfPropertyChange(() => Address);
                }
            }
        }

        public LocationViewModel CurrentLocation { get; set; }
        public LocationViewModel DestinationLocation { get; set; }
        private IDisposable currentGeo;
        private IDisposable stationObs;

        protected override void OnViewAttached(object view, object context)
        {
            base.OnViewAttached(view, context);
            this.view = view as NavigationView;
            this.view.Map.Tap += HandleMapTap;
        }

        void HandleMapTap(object sender, GestureEventArgs e)
        {
            var p = e.GetPosition(view.Map);
            var c = view.Map.ViewportPointToLocation(p);
            LocationHelper.FindAddress(c).Subscribe(addr =>
            {
                if (addr != null)
                    ToLocation = addr.FormattedAddress;
            });
            TakeMeTo(c);
        }

        private void CheckNavigateRequest()
        {
            if (NavigateRequest != null)
            {
                IsFavorite = true;
                ToLocation = NavigateRequest.LocationName;
                if (string.IsNullOrWhiteSpace(ToLocation))
                    ToLocation = NavigateRequest.Address;
                Address = NavigateRequest.Address;
                DestinationLocation.LocationName = NavigateRequest.LocationName;
                DestinationLocation.Coordinate = NavigateRequest.Coordinate;
                if (NavigateRequest.Coordinate != null)
                    TakeMeTo(NavigateRequest.Coordinate);
                else
                    TakeMeTo(NavigateRequest.LocationName);
                NavigateRequest = null;
            }
        }
        protected override void OnActivate()
        {
            base.OnActivate();
            var syncContext = ReactiveExtensions.SyncScheduler;
            if (currentGeo == null)
                currentGeo = LocationHelper.GetCurrentGeoAddress()
                .Where(location => location != null)
                .ObserveOn(syncContext)
                .Subscribe(location =>
                {
                    CurrentLocation.Coordinate = location.Coordinate;
                    FromLocation = location.Address.FormattedAddress;
                });
            
            if (stationObs == null)
                stationObs = cityContext.GetStations()
                    .ObserveOn(ThreadPoolScheduler.Instance) // load stations in background
                    .Where(s => s != null)
                    .Select(s => LocationHelper.GetLocationRect(s))
                    .Where(r => r != null)
                    .ObserveOn(syncContext)
                    .Subscribe(r => this.view.Map.SetView(r));

            CheckNavigateRequest();
        }

        protected override void OnDeactivate(bool close)
        {
            ReactiveExtensions.Dispose(ref currentGeo);
            ReactiveExtensions.Dispose(ref stationObs);
            base.OnDeactivate(close);
        }

        private void FindBestRoute(GeoCoordinate fromLocation, GeoCoordinate toLocation)
        {
            events.Publish(BusyState.Busy("calculating route..."));
            FindNearestStations(fromLocation, toLocation);
        }

        private void HandleAvailabilityError(object sender, ResultCompletionEventArgs e)
        {
            events.Publish(BusyState.NotBusy());
            if (e.Error != null)
                events.Publish(new ErrorState(e.Error, "could not get station availability"));
        }

        private void FindNearestStations(GeoCoordinate fromLocation, GeoCoordinate toLocation)
        {
            cityContext.GetStations().Where(s => s != null)
                .Take(1)
                .ObserveOn(ThreadPoolScheduler.Instance)
                .Subscribe(s  => {
                    StationLocation fromStation = null;
                    StationLocation toStation = null;

                    var nearStart = LocationHelper.SortByLocation(s, fromLocation, toLocation).ToObservable()
                        .ObserveOn(ThreadPoolScheduler.Instance)
                        .Select(station => StationLocationList.GetAvailability2(station).First())
                        .Where(avail => avail.Availability.Available > 0)
                        .Do(avail => fromStation = avail.Station)
                        .Take(1);

                    var nearFinish = LocationHelper.SortByLocation(s, toLocation, fromLocation).ToObservable()
                        .ObserveOn(ThreadPoolScheduler.Instance)
                        .Select(station => StationLocationList.GetAvailability2(station).First())
                        .Where(avail => avail.Availability.Free > 0)
                        .Do(avail => toStation = avail.Station)
                        .Take(1);

                    var route = nearStart.Merge(nearFinish)
                        .ObserveOn(ThreadPoolScheduler.Instance)
                        .Subscribe(null,
                        () =>
                        {
                            IList<GeoCoordinate> navPoints = new List<GeoCoordinate>();
                            navPoints.Add(fromLocation);
                            // if closest bike station is the same as the destination station or the destination is closer than the station then don't use the bikes
                            if (fromStation != toStation
                                && fromLocation.GetDistanceTo(fromStation.Coordinate) < fromLocation.GetDistanceTo(toLocation)
                                && toLocation.GetDistanceTo(toStation.Coordinate) < fromLocation.GetDistanceTo(toLocation))
                            {
                                navPoints.Add(fromStation.Coordinate);
                                navPoints.Add(toStation.Coordinate);
                            }
                            navPoints.Add(toLocation);
                            CalculateRoute(navPoints);
                            events.Publish(BusyState.NotBusy());
                        });
                });
        }

        IEnumerable<GeoCoordinate> navPoints;

        private void CalculateRoute(IEnumerable<GeoCoordinate> navPoints)
        {
            this.navPoints = navPoints;
            LocationHelper.CalculateRoute(navPoints)
                .Subscribe(
                    n => MapRoute(n, null),
                    e => events.Publish(new ErrorState(e, "could not calculate route")),
                    () => events.Publish(BusyState.NotBusy()));
        }

        private void MapRoute(NavigationResponse routeResponse, Exception e)
        {
            try
            {
                if (e != null)
                    events.Publish(new ErrorState(e, "could not calculate route"));
                if (routeResponse == null)
                    return;

                travelDistance = 1000 * routeResponse.Route.TravelDistance;
                travelDuration = routeResponse.Route.TravelDuration;
                if (routeResponse.Route.RouteLegs != null)
                {
                    var routeLegs = routeResponse.Route.RouteLegs;
                    if (routeLegs.Count == 1 || routeLegs.Count == 3)
                    {
                        var walkingDistance = routeLegs[0].TravelDistance;
                        if (routeLegs.Count == 3)
                            walkingDistance += routeLegs[2].TravelDistance;
                        var walkingSpeed = LocationHelper.GetTravelSpeed(TravelType.Walking, config.WalkingSpeed, false);
                        travelDuration = 3600 * walkingDistance / walkingSpeed;
                        if (routeLegs.Count == 3)
                        {
                            var cyclingDistance = routeLegs[1].TravelDistance;
                            var cyclingSpeed = LocationHelper.GetTravelSpeed(TravelType.Cycling, config.CyclingSpeed, false);
                            travelDuration += 3600 * cyclingDistance / cyclingSpeed;
                        }
                        travelDuration = (int)travelDuration;
                    }
                }
                var points = from pt in routeResponse.Route.RoutePath.Points
                             select new GeoCoordinate
                             {
                                 Latitude = pt.Latitude,
                                 Longitude = pt.Longitude
                             };

                MapRoute(points);
            }
            finally
            {
                events.Publish(BusyState.NotBusy());
            }
        }

        private void MapRoute(IEnumerable<GeoCoordinate> points)
        {
            LocationCollection locCol = new LocationCollection();
            foreach (var loc in points)
                locCol.Add(loc);

            LocationRect viewRect = LocationRect.CreateLocationRect(points);

            Execute.OnUIThread(() =>
            {
                MapPolyline pl = new MapPolyline();
                pl.Stroke = new SolidColorBrush(Colors.Blue);
                pl.StrokeThickness = 5;
                pl.Opacity = 0.7;
                pl.Locations = locCol;
                    
                // clear the route and remove pins other than CurrentPos and Destination
                view.RouteLayer.Children.Clear();
                view.RouteLayer.Children.Add(pl);
                view.RoutePinsLayer.Children.Clear();
                    
                int idxPoint = 0;
                int idxDest = navPoints.Count() - 1;
                foreach (var point in navPoints)
                {
                    if (idxPoint == 0)
                        CurrentLocation.Coordinate = point;
                    else if (idxPoint == idxDest)
                        DestinationLocation.Coordinate = point;
                    else
                    {
                        Pushpin pp = new Pushpin();
                        pp.Location = point;
                        double pinWidth = 28;// App.Current.RootVisual.RenderSize.Width * 0.06;
                        Path p = new Path() { Stretch = Stretch.Uniform, Width = pinWidth, Height = pinWidth, Fill = new SolidColorBrush(Colors.White) };
                        Binding b = new Binding();
                        b.Converter = App.Current.Resources["PinTypeToIconConverter"] as IValueConverter;

                        if (idxPoint == 1)
                            pp.DataContext = PinType.BikeStand;
                        else if (idxPoint == 2)
                            pp.DataContext = PinType.Walking;
                        p.SetBinding(Path.DataProperty, b);
                        pp.Content = p;

                        view.RoutePinsLayer.Children.Add(pp);
                    }
                        
                    idxPoint++;
                }
                view.Map.SetView(viewRect);
                    
                view.Map.SetView(LocationRect.CreateLocationRect(points));
                NotifyOfPropertyChange(() => DistanceString);
                NotifyOfPropertyChange(() => DurationString);
                NotifyOfPropertyChange(() => CanToggleFavorite);
                events.Publish(BusyState.NotBusy());
            });
        }

        public void TrySearch()
        {
            TakeMeTo(ToLocation);
        }

        public void TakeMeTo(string address)
        {
            Address = "";
            IsFavorite = false;
            events.Publish(BusyState.Busy("searching..."));
            var localAddress = address;
            if (!string.IsNullOrWhiteSpace(config.City) && !localAddress.ToLowerInvariant().Contains(config.City.ToLowerInvariant()))
                localAddress = localAddress + ", " + config.City;
            LocationHelper.FindLocation(localAddress, CurrentLocation.Coordinate).Subscribe(r =>
            {
                if (r == null || r.Location == null)
                {
                    events.Publish(new ErrorState(new Exception(), "could not find location"));
                    return;
                }
                if (r.Location.Address != null)
                    Address = r.Location.Address.FormattedAddress;
                    
                if (string.IsNullOrEmpty(Address))
                    Address = address;
                this.DestinationLocation.LocationName = address;
                NotifyOfPropertyChange(() => CanToggleFavorite);
                TakeMeTo(new GeoCoordinate(r.Location.Point.Latitude, r.Location.Point.Longitude));
            },
            e => events.Publish(new ErrorState(e, "could not find location")));
        }

        public void TakeMeTo(GeoCoordinate location)
        {
            if (config.LocationEnabled.GetValueOrDefault())
                LocationHelper.GetCurrentLocation().Take(1).Subscribe(c => {
                        CurrentLocation.Coordinate = c.Coordinate;
                        FindBestRoute(c.Coordinate, location);
                },
                e => events.Publish(new ErrorState(e, "could not get current location")));
            else if (CurrentLocation.Coordinate != null)
                FindBestRoute(CurrentLocation.Coordinate, location);
        }

        private bool isFavorite;
        public bool IsFavorite
        {
            get { return isFavorite; }
            set { SetFavorite(value); }
        }
        
        public bool CanToggleFavorite
        {
            get { return !string.IsNullOrWhiteSpace(DestinationLocation.LocationName) || DestinationLocation.Coordinate != null; }
        }

        public void ToggleFavorite()
        {
            SetFavorite(!IsFavorite);
            if (string.IsNullOrWhiteSpace(DestinationLocation.LocationName) && DestinationLocation.Coordinate == null)
                return;
            events.Publish(new FavoriteState(GetFavorite(DestinationLocation), IsFavorite));
        }

        private static FavoriteLocation GetFavorite(LocationViewModel location)
        {
            return new FavoriteLocation(location.LocationName)
            {
                Address = location.Address,
                Coordinate = location.Coordinate
            };
        }

        private void SetFavorite(bool value)
        {
            if (value == isFavorite) return;
            isFavorite = value;
            NotifyOfPropertyChange(() => IsFavorite);
            NotifyOfPropertyChange(() => CanEditName);
        }

        public bool CanEditName
        {
            get { return IsFavorite; }
        }

        public void EditName_()
        {
            LocationViewModel lvm = new LocationViewModel();
            lvm.Address = DestinationLocation.Address;
            lvm.LocationName = DestinationLocation.LocationName;
            IWindowManager wm = IoC.Get<IWindowManager>();
        }

        public IEnumerable<IResult> EditName()
        {
            LocationViewModel lvm = new LocationViewModel();
            if (string.IsNullOrWhiteSpace(Address))
                Address = DestinationLocation.LocationName;
            lvm.Address = DestinationLocation.Address;
            lvm.LocationName = DestinationLocation.LocationName;
            
            var question = new Dialog<Answer>(DialogType.Question,
                "location name",							  
                lvm,
                Answer.Ok,
                Answer.Cancel);

            yield return question.AsResult();

            if (question.GivenResponse == Answer.Ok)
            {
                events.Publish(new FavoriteState(GetFavorite(DestinationLocation), false));
                DestinationLocation.LocationName = lvm.LocationName;
                events.Publish(new FavoriteState(GetFavorite(DestinationLocation), true));
            };
        }
    }
}