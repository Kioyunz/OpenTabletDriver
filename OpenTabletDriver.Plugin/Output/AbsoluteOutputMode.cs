﻿using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Plugin.Output
{
    [PluginIgnore]
    public abstract class AbsoluteOutputMode : BindingHandler, IOutputMode
    {
        private float _halfDisplayWidth, _halfDisplayHeight, _halfTabletWidth, _halfTabletHeight;
        private float _minX, _maxX, _minY, _maxY;
        private Vector2 _min, _max;
        private Matrix3x2 _transformationMatrix;

        private List<IFilter> _filters, _preFilters = new List<IFilter>(), _postFilters = new List<IFilter>();
        public IEnumerable<IFilter> Filters
        {
            set
            {
                _filters = value.ToList();
                _preFilters.Clear();
                _postFilters.Clear();
                foreach (IFilter filter in _filters)
                    if (filter.FilterStage == FilterStage.PreTranspose)
                        _preFilters.Add(filter);
                    else if (filter.FilterStage == FilterStage.PostTranspose)
                        _postFilters.Add(filter);
            }
            get => _filters;
        }

        private DigitizerIdentifier _digitizer;
        public override DigitizerIdentifier Digitizer
        {
            set
            {
                _digitizer = value;
                UpdateCache();
            }
            get => _digitizer;
        }

        private Area _displayArea, _tabletArea;

        public Area Input
        {
            set
            {
                _tabletArea = value;
                UpdateCache();
            }
            get => _tabletArea;
        }

        public Area Output
        {
            set
            {
                _displayArea = value;
                UpdateCache();
            }
            get => _displayArea;
        }

        public IVirtualScreen VirtualScreen { set; get; }
        public abstract IVirtualTablet VirtualTablet { get; }
        public IVirtualPointer Pointer => VirtualTablet;
        public bool AreaClipping { set; get; }
        public bool AreaLimiting { set; get; }

        internal void UpdateCache()
        {
            if (!(Input is null | Output is null | Digitizer is null))
                _transformationMatrix = CalculateTransformation(Input, Output, Digitizer);

            _halfDisplayWidth = Output?.Width / 2 ?? 0;
            _halfDisplayHeight = Output?.Height / 2 ?? 0;
            _halfTabletWidth = Input?.Width / 2 ?? 0;
            _halfTabletHeight = Input?.Height / 2 ?? 0;

            _minX = Output?.Position.X - _halfDisplayWidth ?? 0;
            _maxX = Output?.Position.X + Output?.Width - _halfDisplayWidth ?? 0;
            _minY = Output?.Position.Y - _halfDisplayHeight ?? 0;
            _maxY = Output?.Position.Y + Output?.Height - _halfDisplayHeight ?? 0;

            _min = new Vector2(_minX, _minY);
            _max = new Vector2(_maxX, _maxY);
        }

        internal Matrix3x2 CalculateTransformation(Area input, Area output, DigitizerIdentifier tablet)
        {
            // Convert raw tablet data to millimeters
            var res = Matrix3x2.CreateScale(
                tablet.Width / tablet.MaxX,
                tablet.Height / tablet.MaxY);

            // Translate to the center of input area
            res *= Matrix3x2.CreateTranslation(
                -input.Position.X, -input.Position.Y);

            // Apply rotation
            res *= Matrix3x2.CreateRotation(
                (float)(-input.Rotation * System.Math.PI / 180));

            // Scale millimeters to pixels
            res *= Matrix3x2.CreateScale(
                output.Width / input.Width, output.Height / input.Height);

            // Translate output to virtual screen coordinates
            res *= Matrix3x2.CreateTranslation(
                output.Position.X, output.Position.Y);

            return res;
        }

        public virtual void Read(IDeviceReport report)
        {
            if (report is ITabletReport tabletReport)
            {
                if (Digitizer.ActiveReportID.IsInRange(tabletReport.ReportID))
                {
                    if (VirtualTablet is IPressureHandler pressureHandler)
                        pressureHandler.SetPressure((float)tabletReport.Pressure / (float)Digitizer.MaxPressure);

                    if (Transpose(tabletReport) is Vector2 pos)
                        VirtualTablet.SetPosition(pos);
                }
            }
        }

        internal Vector2? Transpose(ITabletReport report)
        {
            var pos = new Vector2(report.Position.X, report.Position.Y);

            // Pre Filter
            foreach (IFilter filter in _preFilters)
                pos = filter.Filter(pos);

            // Apply transformation
            pos = Vector2.Transform(pos, _transformationMatrix);

            // Clipping to display bounds
            var clippedPoint = Vector2.Clamp(pos, _min, _max);
            if (AreaLimiting && clippedPoint != pos)
                return null;

            if (AreaClipping)
                pos = Vector2.Clamp(pos, _min, _max);

            // Post Filter
            foreach (IFilter filter in _postFilters)
                pos = filter.Filter(pos);

            return pos;
        }
    }
}