using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using WindowsPreview.Kinect;
using Windows.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;



// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace KinectPositionMapper
{
    public enum DisplayFrameType
    {
        Infrared,
        Color,
        Depth,
        BodyMask
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        /// <summary>
        /// The highest value that can be returned in the InfraredFrame.
        /// It is cast to a float for readability in the visualization code
        /// </summary>
        private const float INFRARED_SOURCE_VALUE_MAXIMUM = (float)ushort.MaxValue;

        /// <summary>
        /// Used to set the lower limit, post processing, of the infrared data that 
        /// we will render. Increasing or decreasing this value sets a brightness "wall" 
        /// either closer or further away.
        /// </summary>
        private const float INFRARED_OUTPUT_VALUE_MINIMUM = 0.01f;

        /// <summary>
        /// The upper limit, post processing, of the infrared data that will render.
        /// </summary>
        private const float INFRARED_OUTPUT_VALUE_MAXIMUM = 1.0f;

        /// <summary>
        /// The INFRARED_SCENE_VALUE_AVERAGE value specifies the average
        /// infrared value of the scene. This value was selected by analyzing the 
        /// average pixel intensity for a given scene. This could be calculated at
        /// runtime to handle different IR conditions of a scene (outside vs inside)
        /// </summary>
        private const float INFRARED_SCENE_VALUE_AVERAGE = 0.08f;

        /// <summary>
        /// The INFRARED_SCENE_STANDARD_DEVIATIONS value specifies the number
        /// of standard deviations to apply to the INFRARED_SCENE_VALUE_AVERAGE.
        /// This value was selected by analyzing data from a given scene. This could
        /// be calculated at runtime to handle different IR conditions of a scene.
        /// </summary>
        private const float INFRARED_SCENE_STANDARD_DEVIATIONS = 3.0f;

        private const DisplayFrameType DEFAULT_DISPLAY_FRAME_TYPE = DisplayFrameType.Color;

        private KinectSensor kinectSensor = null;
        private string statusText = null;

        // Size of RGB pixel in bitmap
        private const int BYTES_PER_PIXEL = 4;
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;
        private DisplayFrameType currentDisplayFrameType;
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private CoordinateMapper coordinateMapper = null;

        //Infrared Frame
        private ushort[] infraredFrameData = null;
        private byte[] infraredPixels = null;

        //Depth Frame
        private ushort[] depthFrameData = null;
        private byte[] depthPixels = null;

        //BodyMask Frame
        private DepthSpacePoint[] colorMappedToDepthPoints = null;

        public event PropertyChangedEventHandler PropertyChanged;
        public string StatusText
        {
            get { return this.statusText;  }
            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription;  }
            set
            {
                if (this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }
       

        public MainPage()
        {
            // Initializing kinectSensor
            this.kinectSensor = KinectSensor.GetDefault();

            SetupCurrentDisplay(DEFAULT_DISPLAY_FRAME_TYPE);

            // getting the FrameDescription from multiple sources
            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

            this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(
                FrameSourceTypes.Infrared 
                | FrameSourceTypes.Color 
                | FrameSourceTypes.Depth
                | FrameSourceTypes.BodyIndex);
            this.multiSourceFrameReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;
        

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // use the window object as the view model in this example
            this.DataContext = this;

            // open sensor
            this.kinectSensor.Open();

            this.InitializeComponent();

        }

        private void Reader_MultiSourceFrameArrived(MultiSourceFrameReader sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            // If the frame has expired by the time we process this event, return.
            if (multiSourceFrame == null)
            {
                return;
            }
            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            InfraredFrame infraredFrame = null;
            BodyIndexFrame bodyIndexFrame = null;
            IBuffer depthFrameDataBuffer = null;
            IBuffer bodyIndexFrameData = null;
            IBufferByteAccess bodyIndexByteAccess = null;
            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.Infrared:
                    using (infraredFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame())
                    {
                        ShowInfraredFrame(infraredFrame);
                    }
                    break;
                case DisplayFrameType.Color:
                    using (colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame())
                    {
                        ShowColorFrame(colorFrame);
                    }
                    break;
                case DisplayFrameType.Depth:
                    using (depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame())
                    {
                        ShowDepthFrame(depthFrame);
                    }
                    break;
                case DisplayFrameType.BodyMask:
                    try
                    {
                        depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame();
                        colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();
                        bodyIndexFrame = multiSourceFrame.BodyIndexFrameReference.AcquireFrame();
                        if ((depthFrame == null) || (colorFrame == null) || (bodyIndexFrame == null))
                        {
                            return;
                        }

                        // Access the depth frame data directly via LockImageBuffer to avoid making a copy
                        depthFrameDataBuffer = depthFrame.LockImageBuffer();
                        this.coordinateMapper.MapColorFrameToDepthSpaceUsingIBuffer(depthFrameDataBuffer, this.colorMappedToDepthPoints);
                        // Process color
                        colorFrame.CopyConvertedFrameDataToBuffer(this.bitmap.PixelBuffer, ColorImageFormat.Bgra);
                        // Access the body index frame data directly via LockImageBuffer to avoid making a copy
                        bodyIndexFrameData = bodyIndexFrame.LockImageBuffer();
                        ShowMappedBodyFrame(depthFrame.FrameDescription.Width, depthFrame.FrameDescription.Height, bodyIndexFrameData, bodyIndexByteAccess);
                    }
                    finally
                    {
                        if (depthFrame != null)
                        {
                            depthFrame.Dispose();
                        }
                        if (colorFrame != null)
                        {
                            colorFrame.Dispose();
                        }
                        if (bodyIndexFrame != null)
                        {
                            bodyIndexFrame.Dispose();
                        }
                        if (depthFrameDataBuffer != null)
                        {
                            // We must force a release of the IBuffer in order to ensure that we have
                            // dropped all references to it.
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(depthFrameDataBuffer);
                        }
                        if (bodyIndexFrameData != null)
                        {
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(bodyIndexFrameData);
                        }
                        if (bodyIndexByteAccess != null)
                        {
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(bodyIndexByteAccess);

                        }
                    }
                    break;
                default:
                    break;
            }
        }

        private void SetupCurrentDisplay(DisplayFrameType newDisplayFrameType)
        {
            currentDisplayFrameType = newDisplayFrameType;
            // Frames used by more than one type are declared outside the switch statement
            FrameDescription colorFrameDescription = null;
            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.Infrared:
                    FrameDescription infraredFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
                    this.CurrentFrameDescription = infraredFrameDescription;
                    // allocate space to put the pixels being received and converted
                    this.infraredFrameData = new ushort[infraredFrameDescription.Width * infraredFrameDescription.Height];
                    this.infraredPixels = new byte[infraredFrameDescription.Width * infraredFrameDescription.Height * BYTES_PER_PIXEL];
                    this.bitmap = new WriteableBitmap(infraredFrameDescription.Width, infraredFrameDescription.Height);
                    break;

                case DisplayFrameType.Color:
                    colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
                    this.CurrentFrameDescription = colorFrameDescription;
                    // create the bitmap
                    this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height);
                    break;
                case DisplayFrameType.Depth:
                    FrameDescription depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
                    this.CurrentFrameDescription = depthFrameDescription;
                    this.depthFrameData = new ushort[depthFrameDescription.Width * depthFrameDescription.Height];
                    this.depthPixels = new byte[depthFrameDescription.Width * depthFrameDescription.Height * BYTES_PER_PIXEL];
                    this.bitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height);
                    break;
                case DisplayFrameType.BodyMask:
                    colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
                    this.CurrentFrameDescription = colorFrameDescription;
                    this.colorMappedToDepthPoints = new DepthSpacePoint[colorFrameDescription.Width * colorFrameDescription.Height];
                    this.bitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height);
                    break;
                default:
                    break;
            }
        }

        private void Sensor_IsAvailableChanged(KinectSensor sender, IsAvailableChangedEventArgs args)
        {
            this.StatusText = this.kinectSensor.IsAvailable ? "Running" : "Not Available";
        }

        unsafe private void ShowMappedBodyFrame(int depthWidth, int depthHeight, IBuffer bodyIndexFrameData, IBufferByteAccess bodyIndexByteAccess)
        {
            bodyIndexByteAccess = (IBufferByteAccess)bodyIndexFrameData;
            byte* bodyIndexBytes = null;
            bodyIndexByteAccess.Buffer(out bodyIndexBytes);

            fixed (DepthSpacePoint* colorMappedToDepthPointsPointer = this.colorMappedToDepthPoints)
            {
                IBufferByteAccess bitmapBackBufferByteAccess = (IBufferByteAccess)this.bitmap.PixelBuffer;

                byte* bitmapBackBufferBytes = null;
                bitmapBackBufferByteAccess.Buffer(out bitmapBackBufferBytes);
                // treat the color data as 4-byte pixels
                uint* bitmapPixelsPointer = (uint*)bitmapBackBufferBytes;

                // Loop over each row and column of the color image
                // Zero out any pixels that don't correspond to a body index
                int colorMappedLength = this.colorMappedToDepthPoints.Length;
                for (int colorIndex= 0; colorIndex < colorMappedLength; colorIndex++)
                {
                    float colorMappedToDepthX = colorMappedToDepthPointsPointer[colorIndex].X;
                    float colorMappedToDepthY = colorMappedToDepthPointsPointer[colorIndex].Y;

                    // The sentinel value is -inf, -inf meaning that no depth pixel corresponds to this color pixel.
                    if (!float.IsNegativeInfinity(colorMappedToDepthX) && !float.IsNegativeInfinity(colorMappedToDepthY))
                    {
                        int depthX = (int)(colorMappedToDepthX + 0.5f);
                        int depthY = (int)(colorMappedToDepthY + 0.5f);

                        // If the point is not valid, there is no body index there.
                        if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
                        {
                            int depthIndex = (depthY * depthWidth) + depthX;

                            // IF we are tracking a body for the current pixel do not zero out pixel
                            if (bodyIndexBytes[depthIndex] != 0xff)
                            {
                                // this bodyINdexByte is good and is abody
                                continue;
                            }
                        }
                    }
                    // this pixel does not correspond to a body so make it black and transparent
                    bitmapPixelsPointer[colorIndex] = 0;
                }
            }

            this.bitmap.Invalidate();
            FrameDisplayImage.Source = this.bitmap;
        }

        private void ShowInfraredFrame(InfraredFrame infraredFrame)
        {
            bool infraredFrameProcessed = false;

            if (infraredFrame != null)
            {
                FrameDescription infraredFrameDescription = infraredFrame.FrameDescription;

                // verify data and write the new infrared frame data to the display bitmap
                if (((infraredFrameDescription.Width * infraredFrameDescription.Height) == this.infraredFrameData.Length) &&
                    (infraredFrameDescription.Width == this.bitmap.PixelWidth) &&
                    (infraredFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    // Copy the pixel data from the image to a temporary array
                    infraredFrame.CopyFrameDataToArray(this.infraredFrameData);

                    infraredFrameProcessed = true;
                }
            }

            // we got a frame, convert and render

            if (infraredFrameProcessed)
            {
                ConvertInfraredDataToPixels();
                RenderPixelArray(this.infraredPixels);
            }
        }

        private void ShowColorFrame(ColorFrame colorFrame)
        {
            bool colorFrameProcessed = false;

            if (colorFrame != null)
            {
                FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                if ((colorFrameDescription.Width == this.bitmap.PixelWidth) && (colorFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    if (colorFrame.RawColorImageFormat == ColorImageFormat.Bgra)
                    {
                        colorFrame.CopyRawFrameDataToBuffer(this.bitmap.PixelBuffer);
                    } else
                    {
                        colorFrame.CopyConvertedFrameDataToBuffer(this.bitmap.PixelBuffer, ColorImageFormat.Bgra);
                    }

                    colorFrameProcessed = true;
                }
            }

            if (colorFrameProcessed)
            {
                this.bitmap.Invalidate();
                FrameDisplayImage.Source = this.bitmap;
            }
        }

        private void ShowDepthFrame(DepthFrame depthFrame)
        {
            bool depthFrameProcessed = false;
            ushort minDepth = 0;
            ushort maxDepth = 0;

            if (depthFrame != null)
            {
                FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                // verify data and write frame data to display bitmap
                if (((depthFrameDescription.Width * depthFrameDescription.Height) == this.depthFrameData.Length) &&
                    (depthFrameDescription.Width == this.bitmap.PixelWidth) && (depthFrameDescription.Height == this.bitmap.PixelHeight))
                {
                    depthFrame.CopyFrameDataToArray(this.depthFrameData);

                    minDepth = depthFrame.DepthMinReliableDistance;
                    maxDepth = depthFrame.DepthMaxReliableDistance;

                    depthFrameProcessed = true;
                }
            }

            if (depthFrameProcessed == true)
            {
                ConvertDepthDataToPixels(minDepth, maxDepth);
                RenderPixelArray(this.depthPixels);
            }
        }

        private void ConvertDepthDataToPixels(ushort minDepth, ushort maxDepth)
        {
            int colorPixelIndex = 0;
            int mapDepthToByte = maxDepth / 256;

            for (int i = 0; i < this.depthFrameData.Length; i++)
            {
                // depth for this pixel
                ushort depth = this.depthFrameData[i];

                byte intensity = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / mapDepthToByte) : 0);
                for (int j = 0; j < 3; j++)
                {
                    this.depthPixels[colorPixelIndex++] = intensity;
                }
                this.depthPixels[colorPixelIndex++] = 255;
            }
        }

        private void ConvertInfraredDataToPixels()
        {
            // convert the infrared to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < this.infraredFrameData.Length; i++)
            {
                // normalize the incoming infrared data (ushort) to a float
                // ranging from INFRARED_OUTPUT_VALUE_MINIMUM to 
                // INFRARED_OUTPUT_VALUE_MAXIMUM

                // 1. dividing the incoming value by source max
                float intensityRatio = (float)this.infraredFrameData[i] / INFRARED_SOURCE_VALUE_MAXIMUM;

                // 2. dividing by the (average scene value * standard deviations)
                intensityRatio /= INFRARED_SCENE_VALUE_AVERAGE * INFRARED_SCENE_STANDARD_DEVIATIONS;

                // 3. limiting the value to Maximum
                intensityRatio = Math.Min(INFRARED_OUTPUT_VALUE_MAXIMUM, intensityRatio);

                // 4. limiting lower bound
                intensityRatio = Math.Max(INFRARED_OUTPUT_VALUE_MINIMUM, intensityRatio);

                // 5. Convert normalized value to byte and use result as RGB components
                // required by image

                byte intensity = (byte)(intensityRatio * 225.0f);
                this.infraredPixels[colorPixelIndex++] = intensity; // Blue
                this.infraredPixels[colorPixelIndex++] = intensity; // Green
                this.infraredPixels[colorPixelIndex++] = intensity; // Red
                this.infraredPixels[colorPixelIndex++] = 255; // Alpha
            }
        }

        private void RenderPixelArray(byte[] pixels)
        {
            pixels.CopyTo(this.bitmap.PixelBuffer);
            this.bitmap.Invalidate();
            FrameDisplayImage.Source = this.bitmap;

        }

        private void InfraredButton_OnClick(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Infrared);
        }

        private void ColorButton_OnClick(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Color);
        }

        private void DepthButton_OnClick(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.Depth);
        }

        private void BodyMask_OnClick(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DisplayFrameType.BodyMask);
        }
    }

    [Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBufferByteAccess
    {
        unsafe void Buffer(out byte* pByte);
    }
}
