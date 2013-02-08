﻿using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Phone.Controls;


namespace Bicikelj.Controls
{
	[TemplatePart(Name = "ContentContainer", Type = typeof(object))]
	public class HubTileWithContent : HubTile
	{
		void HubTileWithContent_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
			throw new NotImplementedException();
		}

		public object Content
		{
			get { return (object)GetValue(ContentProperty); }
			set { SetValue(ContentProperty, value); }
		}

		// Using a DependencyProperty as the backing store for Content.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty ContentProperty =
			DependencyProperty.Register("Content", typeof(object), typeof(HubTileWithContent), new PropertyMetadata(null));


		public HubTileWithContent() : base()
		{
			DefaultStyleKey = typeof(HubTileWithContent);
			this.IsEnabledChanged += (sender, e) =>
			{
				if (IsEnabled)
					Background = (Brush)Application.Current.Resources["PhoneAccentBrush"];
				else
					Background = (Brush)Application.Current.Resources["PhoneDisabledBrush"];
			};
		}

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			//var obj = this.GetTemplateChild("ContentContainer");
		}
	}
}
