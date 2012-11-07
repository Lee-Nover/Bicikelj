﻿using Caliburn.Micro;
using System.Collections.Generic;
using Bicikelj.Model;
using System.Windows;
using Microsoft.Phone.Controls.Maps;
using System.Device.Location;
using System.Linq;
using System;
using Bicikelj.Model.Bing;
using Bicikelj.Views;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Bicikelj.ViewModels
{
	public class NavigationViewModel : Screen
	{
		readonly IEventAggregator events;
		public NavigationViewModel(IEventAggregator events, StationLocationList stationList)
		{
			this.events = events;
			this.stationList = stationList;
		}

		private StationLocationList stationList;

		private double travelDistance = double.NaN;
		private double travelDuration = double.NaN;
		private NavigationView view;

		public string DistanceString
		{
			get
			{
				if (double.IsNaN(travelDistance))
					return "travel distance not available";
				else
					return string.Format("travel distance {0}", LocationHelper.GetDistanceString(travelDistance, false));
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

		public string ToLocation { get; set; }
		public GeoCoordinate CurrentLocation { get; set; }

		protected override void OnViewAttached(object view, object context)
		{
			base.OnViewAttached(view, context);
			this.view = view as NavigationView;
			stationList.GetStations((s, e) => {
				this.view.Map.SetView(stationList.LocationRect);
			});
			this.view.Map.SetView(stationList.LocationRect);
			
			//ToLocation = "Triglavska 21, Ljubljana";
			GetCoordinate.Current((c, e) => {
				CurrentLocation = c;
				//TrySearch();
			});
		}

		private void FindBestRoute(GeoCoordinate fromLocation, GeoCoordinate toLocation)
		{
			events.Publish(new BusyState(true, "calculating route..."));
			if (stationList.Stations == null)
			{
				stationList.GetStations((s, e) =>
				{
					if (e != null)
						Execute.OnUIThread(() => { MessageBox.Show(e.Message); });
					else if (s != null)
						FindNearestStations(fromLocation, toLocation);
				});
				return;
			}
			else
				FindNearestStations(fromLocation, toLocation);
		}

		private void HandleAvailabilityError(object sender, ResultCompletionEventArgs e)
		{
			events.Publish(new BusyState(false));
			if (e.Error != null)
				Execute.OnUIThread(() => { MessageBox.Show(e.Error.Message); });
		}

		private void FindNearestStations(GeoCoordinate fromLocation, GeoCoordinate toLocation)
		{
			// find closes available bike
			StationAvailabilityHelper.CheckStations(stationList.SortByLocation(fromLocation, toLocation), (fromStation, fromAvail) =>
			{
				bool availableResult = fromAvail != null && fromAvail.Available > 0;
				if (availableResult)
				{
					// find closes free stand
					StationAvailabilityHelper.CheckStations(stationList.SortByLocation(toLocation, fromLocation), (toStation, toAvail) =>
						{
							bool freeResult = toAvail != null && toAvail.Free > 0;
							if (freeResult)
							{
								Execute.OnUIThread(() =>
								{
									var navPoints = new GeoCoordinate[] { fromLocation, fromStation.Coordinate, toStation.Coordinate, toLocation };
									RouteMap(navPoints);
									events.Publish(new BusyState(false));
								});
							}
							return freeResult;
						}, HandleAvailabilityError);
				}
				return availableResult;
			}, HandleAvailabilityError);
		}

		IEnumerable<GeoCoordinate> navPoints;
		private void RouteMap(IEnumerable<GeoCoordinate> navPoints)
		{
			this.navPoints = navPoints;
			LocationHelper.CalculateRoute(navPoints, MapRoute);
		}

		private void MapRoute(NavigationResponse routeResponse, Exception e)
		{
			try
			{
				if (e != null)
					Execute.OnUIThread(() => { MessageBox.Show(e.Message); });
				if (routeResponse == null)
					return;

				travelDistance = 1000 * routeResponse.Route.TravelDistance;
				travelDuration = routeResponse.Route.TravelDuration;
				var points = from pt in routeResponse.Route.RoutePath.Points
							 select new GeoCoordinate
							 {
								 Latitude = pt.Latitude,
								 Longitude = pt.Longitude
							 };
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
					view.Map.Children.Clear();
					view.Map.Children.Add(pl);
					int idxPoint = 0;
					foreach (var point in navPoints)
					{
						Pushpin pp = new Pushpin();
						pp.Location = point;
						switch (idxPoint++)
						{
							case 0:
								pp.Content = new Image() { Source = new BitmapImage(new Uri("/Images/PersonPushpin3.png", UriKind.Relative)), Stretch = Stretch.None };
								break;
							case 1:
								pp.Content = new Image() { Source = new BitmapImage(new Uri("/Images/BikePushpin3.png", UriKind.Relative)), Stretch = Stretch.None };
								break;
							case 2:
								pp.Content = new Image() { Source = new BitmapImage(new Uri("/Images/WalkingPushpin.png", UriKind.Relative)), Stretch = Stretch.None };
								break;
							case 3:
								pp.Content = new Image() { Source = new BitmapImage(new Uri("/Images/FinishPushpin.png", UriKind.Relative)), Stretch = Stretch.None };
								break;
							default:
							break;
						}
						
						view.Map.Children.Add(pp);
					}
					view.Map.SetView(viewRect);
					
					view.Map.SetView(LocationRect.CreateLocationRect(points));
					NotifyOfPropertyChange(() => DistanceString);
					NotifyOfPropertyChange(() => DurationString);
					events.Publish(new BusyState(false));
				});
			}
			finally
			{
				events.Publish(new BusyState(false));
			}
		}

		public void TrySearch()
		{
			TakeMeTo(ToLocation);
		}

		public void TakeMeTo(string address)
		{
			events.Publish(new BusyState(true, "searching..."));
			// todo get the address coordinates using bing maps
			LocationHelper.FindLocation(address, CurrentLocation, (r, e) =>
			{
				if (e != null)
					Execute.OnUIThread(() => { MessageBox.Show(e.Message); });
				else if (r != null && r.Location != null)
					TakeMeTo(new GeoCoordinate(r.Location.Point.Latitude, r.Location.Point.Longitude));
			});
		}

		public void TakeMeTo(GeoCoordinate location)
		{
			GetCoordinate.Current((c, e) => {
				if (e != null)
				{
					events.Publish(new BusyState(false));
					Execute.OnUIThread(() => { MessageBox.Show(e.Message); });
				}
				else
				{
					CurrentLocation = c;
					FindBestRoute(c, location);
				}
			});
		}
	}
}