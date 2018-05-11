/****************************************************************************
While the underlying libraries are covered by LGPL, this sample is released 
as public domain.  It is distributed in the hope that it will be useful, but 
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
or FITNESS FOR A PARTICULAR PURPOSE.  
*****************************************************************************/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Drawing.Imaging;

using LeopardCamera;
using PluginInterface;

using EmguTool;

namespace CameraTool
{
    public partial class CameraToolForm : Form
    {
        internal LPCamera capture = null;
        private enum CameraType { NO_CAMERA, CYPRESS_USB_BOOT, LEOPARD_CAMERA };
        private CameraType cameraList;

        // M034 modes for 720p
        private enum M034SensorMode { M720P_30_HDR_DLO = 0x06, M720P_30_HDR_MC, M720P_55_HDR_MC, M720P_55_HDR_DLO, M720P_30_SDR, M720P_60_SDR };
        private enum PIXEL_ORDER {GBRG = 0, GRBG, BGGR, RGBG };

        private string CameraUUID, FuseID;
        private UInt16 HwRev, FwRev;
        private UInt16 ROIX_MAX, ROIX_MIN, ROIY_MAX, ROIY_MIN;
        private bool MarkEn = false;

        private LeopardCamera.LPCamera.SENSOR_DATA_MODE m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV;

        private int m_CameraIndex = 0, m_ResolutionIndex = 0, m_FrameRateIndex = 0;

        private bool mRAWDisplay = true;
        private bool m_TriggerMode = false;
        private string g_Image_Extension = "jpg"; // YKB 20180507 增加存储格式选择,0 bmp;1 jpg;2 tif;3 gif;4 png
        private bool m_CaptureOneImage = false;
        private bool m_SaveAllImage = false;      //Modify by PMH 2018.4.12
        private bool m_AutoExposure = false;
        private bool m_MonoSensor = false;
        private bool m_AutoTrigger = false;
        private int m_AutoTriggerCnt = 0, m_AutoTriggerPrevCnt = 0;
        private PIXEL_ORDER m_pixelOrder;
        private bool m_NoiseCalculationEna = false; // YKB 20180421 modify 初始化不计算噪声

        private double m_ImageMean, m_RectMean, m_RectSTD, m_RectTN, m_RectFPN; // STD is total noise, TN is Temporal Noise, FPN is Fixed Pattern Noise
        private double m_PrevImageMean;
        private double dTargetMean;
        private double dTargetMeanFactor = 1.2;
        private int m_curExpTimeInLines=500; // exposure time, measured in Lines
        private int m_curExp=0;
        private int m_curGain=8;
        private int m_curExpXGain;
        private bool m_AE_done = false;
        private int m_PrevFrameCnt = 0;
        private bool m_Show_Anchors = false;
        private bool m_Show_Grid = false; // YKB 20180427 增加十字丝显示

        private bool m_RW_REG_ModeSET = false;
        private int W_address = 0, W_value = 0;
        private int R_address = 0;
        private int ROI_StartX = 0;
        private int ROI_StartY = 0;

        public int R_value = 0;

        private int SensorMode = 0;
        private int Roi_Level = 0;

        private RegRW_ModeSET frmRegRW_MODESET;

        private uint m_delayTime = 0;
        private SetTriggerDelay frmSetTriggerDelay;

        private CameraPropWin frmCameraPropWin;
        private bool m_curAE = false;

        private bool FrameDisconntinued = false;
        private bool FlashUpdateInProgress = false;

        private int flashUpdatePercentage = 0;

        // emguCV demo
        private EmguTool.EmguDemo.EmguDemoId m_EmguDemoId = EmguTool.EmguDemo.EmguDemoId.DisableDemo; 

        private LeopardPlugin m_selectedPlugin;
        private ICollection<LeopardPlugin> m_plugins;

        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        Thread thread; // YKB 20180423 add
        // 图像保存部分变量
        private byte[] imageArray, imageArrayPre;
        private Bitmap imageBmp;
        private bool m_SaveFrameToFile = false;
        private bool m_SaveFileInProcess = false;
        
        private Bitmap imageBmpSave;
        int iLastFrameCount = 0;  // YKB 20180425 add 记录保存图像时相机帧数
        int iNumDiff = 0; // YKB 20180425 add 记录图像保存次数
        string g_ConfigPath = ""; // YKB 20180428 配置文件路径
        string g_SavePath = ""; // YKB 20180510 图片保存路径
        string g_SaveSuffix = ""; // YKB 20180510 文件后缀

        ImageCodecInfo g_ImageCodecInfo;
        EncoderParameters g_EncoderParameters; // 图片编码参数

        public CameraToolForm()
        {
            InitializeComponent(); // YKB 20180420 菜单初始化

            AddPluginMenu(); // YKB 20180420 初始化插件菜单显示

            frmRegRW_MODESET = new RegRW_ModeSET(); // YKB 20180420 参数设置类初始化
            frmSetTriggerDelay = new SetTriggerDelay();
            frmCameraPropWin = new CameraPropWin();
            frmCameraPropWin.Gain.curValue = m_curGain;
            frmCameraPropWin.AE = m_curAE;
            frmCameraPropWin.Exposure.curValue = m_curExp;
            frmCameraPropWin.ExpTime = m_curExpTimeInLines;

            W_address = frmRegRW_MODESET.WReg_Address;
            W_value = frmRegRW_MODESET.WReg_Value;
            R_address = frmRegRW_MODESET.RReg_Address;
            R_value = frmRegRW_MODESET.RReg_Value;

            SensorMode = frmRegRW_MODESET.sensormode;

            thread = new Thread(thread_saveimage); // YKB 20180423 add 为存图专门新建一个线程
            //thread.IsBackground = true;
            thread.Start();

            timer.Tick += new EventHandler(timer_Tick); // Everytime timer ticks, timer_Tick will be called
            timer.Interval = (10);
            timer.Enabled = true;                       // Enable the timer
            timer.Start();                              // Start the timer

            startTime = DateTime.Now;

            DetectCamera();
        }
        private void thread_saveimage() // YKB 20180423 add 为存图专门新建一个线程
        {
            string SubPath = DateTime.Now.ToString("yyyyMMddhhmmss"); // YKB 20180507 连续存图时的子文件夹前缀
            while (true)
            {
                Thread.Sleep(1);
                //System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                //watch.Start();  //开始监视代码运行时间
                //TimeSpan timespan = watch.Elapsed;  //获取当前实例测量得出的总时间
                //System.Diagnostics.Debug.WriteLine("代码执行时间：{0}(毫秒)", timespan.TotalMilliseconds);  //总毫秒数

                //*************************************连续存储***********************************************
                if (m_SaveFrameToFile && m_SaveAllImage) // 连续存图和图像获取完成，则进入图像保存
                {
                    string SavePath = g_SavePath + "\\" + SubPath + "_" + (iNumDiff / 3000).ToString("D3") + "\\"; // YKB 20180504 由于保存图片数据量太大时对于读写性能有影响，因此一定数据量文件之后切换路径
                    if (!Directory.Exists(SavePath))//判断是否存在
                    {
                        Directory.CreateDirectory(SavePath);//创建新路径
                    }
                    string FileName = SavePath + iNumDiff.ToString("D6") + g_SaveSuffix;
                    FileName = Path.ChangeExtension(FileName, g_Image_Extension);

                    imageBmpSave.Save(FileName, g_ImageCodecInfo, g_EncoderParameters);
                    imageBmpSave.Dispose(); // 释放空间，因为在图像获取时创建了空间
                    m_SaveFrameToFile = false; // 先交给回调函数采集图像，同时该线程保存图片
                }
                //*************************************连续存储结束***********************************************

                //*************************************单帧存储***********************************************
                if (m_SaveFrameToFile && m_CaptureOneImage) // 单帧存图和图像获取完成，则进入图像保存
                {
                    string SavePath = g_SavePath;
                    if (!Directory.Exists(SavePath))//判断是否存在
                    {
                        Directory.CreateDirectory(SavePath);//创建新路径
                    }
                    string FileName = SavePath + "\\" + DateTime.Now.ToString("yyyyMMddhhmm_ss_fff");
                    FileName = Path.ChangeExtension(FileName, g_Image_Extension);

                    imageBmpSave.Save(FileName, g_ImageCodecInfo, g_EncoderParameters);
                    imageBmpSave.Dispose();
                    m_CaptureOneImage = false; // 保存一次则退出
                    m_SaveFrameToFile = false;
                }
                //*************************************单帧存储结束***********************************************

            }
        }

        private void AddPluginMenu()
        {

            m_plugins = LoadPlugins("Plugins");

            try
            {
                foreach (var item in m_plugins)
                {
                    ToolStripMenuItem NEW;
                    NEW = new ToolStripMenuItem(item.Name);
                    NEW.Text = item.Name;
                    NEW.Click += new EventHandler(pluginsStripMenuItemClick);
                    pluginsToolStripMenuItem.DropDown.Items.Add(NEW);
                }
            }
            catch
            {
            }
        }

        private void pluginsStripMenuItemClick(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                m_selectedPlugin = null;
                foreach (var plugin in m_plugins)
                {
                    if (plugin.Name == sender.ToString())
                    {
                        m_selectedPlugin = plugin;
                        m_selectedPlugin.SetCameraId(capture.cameraModel);
                        m_selectedPlugin.Initialize();

                        if (m_SensorDataMode != LPCamera.SENSOR_DATA_MODE.YUV
                            && m_SensorDataMode != LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
                            m_AutoExposure = true; // enable auto exposure

                       // if (capture.cameraModel == LPCamera.CameraModel.IMX172)
                        {
                            mRAWDisplay = false;
                            ToolStripMenuItem item = (ToolStripMenuItem)noDisplayToolStripMenuItem;
                            item.Checked = true;
                        }
                        break;
                    }
                }

            }
        }

        private void PluginProcess(IntPtr pBuffer, int width, int height, int bpp, 
            LeopardCamera.LPCamera.SENSOR_DATA_MODE sensorMode, bool monoSensor, int pixelOrder, int exposureTime, string cameraID)
        {
            if (m_selectedPlugin != null)
            {
                m_selectedPlugin.Process(pBuffer, width, height, bpp, sensorMode, monoSensor, pixelOrder, exposureTime, cameraID);
            }
        }

        private int preFrameCount = 0;
        private DateTime startTime, endTime;
        private DateTime triggerStartTime, triggerEndTime;
        void timer_Tick(object sender, EventArgs e)
        {
            if (capture != null) // YKB 20180420 一旦有相机连接，实例化成功则进入
            {
                endTime = DateTime.Now;
                TimeSpan ts = endTime - startTime;

                if (ts.TotalSeconds > 2) // YKB 20180420 每2秒刷新一次帧率
                {
                    int fps = capture.FrameCount - preFrameCount;
                    preFrameCount = capture.FrameCount;

                    if (fps >= 0)
                        toolStripStatusLabelFPS.Text = ((double)fps * 1000 / (ts.Seconds * 1000 + ts.Milliseconds)).ToString("F1") + " fps";

                    startTime = DateTime.Now;
                }

                //if (!m_SaveFileInProcess) // YKB 20180420 是否正在保存图片 true 正在保存图片
                //{
                //    //System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
                //    //watch.Start();  //开始监视代码运行时间
                //    SaveCapturedImage();
                //    //TimeSpan timespan = watch.Elapsed;  //获取当前实例测量得出的总时间
                //    //System.Diagnostics.Debug.WriteLine("打开窗口代码执行时间：{0}(毫秒)", timespan.TotalMilliseconds);  //总毫秒数
                //}

                if (m_AutoExposure) // YKB 20180420 是否自动曝光
                {
                    if (capture.FrameCount - m_PrevFrameCnt > 3)
                    {
                        doAE();
                        m_PrevFrameCnt = capture.FrameCount;

                        statusToolStripStatusLabel.Text = "Frame Count: " + capture.FrameCount.ToString() // YKB 20180423 add 帧率显示
                                + "  Mean: " + m_ImageMean.ToString("F1")
                                + "  exp: " + m_curExpTimeInLines.ToString()
                                + "  gain: " + m_curGain.ToString()
                                + "  AE doen:  " + m_AE_done.ToString();
                    }
                }
                else // YKB 20180423 非自动曝光
                {
                    if (capture.FrameCount != m_PrevFrameCnt) // org code
                    {
                        statusToolStripStatusLabel.Text = "Frame Count: " + capture.FrameCount.ToString();
                    }
                    savecounttoolStripStatusLabel.Text = iNumDiff.ToString("D6") + " ";

                    m_PrevFrameCnt = capture.FrameCount;

                    if (FrameDisconntinued)
                        statusToolStripStatusLabel.Text += "FrmCnt disconnectinued";
                }

                // update the progress
                if (FlashUpdateInProgress)
                {
                    statusToolStripStatusLabel.Text = " Flash update at " + flashUpdatePercentage.ToString() + "%";
                }

                if (m_AutoTrigger)
                {
                    // captured one frame
                    if (m_AutoTriggerPrevCnt != m_AutoTriggerCnt)
                    {
                        TimeSpan tsAutoTrigger = triggerEndTime - triggerStartTime;
                        statusToolStripStatusLabel.Text += "  Capture Latency: " + (tsAutoTrigger.Seconds * 1000 + tsAutoTrigger.Milliseconds).ToString() + " ms"; // Capture Latency (including exposure time)

                        m_AutoTriggerPrevCnt = m_AutoTriggerCnt;

                        capture.SoftTrigger();
                        triggerStartTime = DateTime.Now;
                    }
                }
                if (m_NoiseCalculationEna)
                {
                    toolStripStatusLabelMean.Text = m_RectMean.ToString("F1");
                    toolStripStatusLabelSTD.Text = m_RectSTD.ToString("F1");
                }
                else
                {
                    toolStripStatusLabelMean.Text = "-";
                    toolStripStatusLabelSTD.Text = "-";
                }
                toolStripStatusLabelTN.Text = "-";// m_RectTN.ToString("F1");
                //toolStripStatusLabelFPN.Text = "-";// m_RectFPN.ToString("F1");

                //*************************************参数设置***********************************************
                // 参数在其他地方获取，统一在此处设置
                handleTriggerDelayTime();
                handleRegRW_MODESET();
                //handleCameraPropWin();
                parsePluginParam();
                //*************************************参数设置结束***********************************************
            }
            else // YKB 20180420 相机不存在则帧率为0
                toolStripStatusLabelFPS.Text = "0.0";
        }

        private void doAE()
        {
            if (m_AutoExposure && capture != null)
            {

                int maxExposureTime = capture.Height * 4;
                int minExposureTime = 50;
                int minGain = 8;
                int maxGain = 63;

                switch (capture.cameraModel)
                {
                    case LPCamera.CameraModel.ICX285:
                        minGain = 1;
                        maxGain = 8;
                        minExposureTime = 1;
                        break;
                    case LPCamera.CameraModel.IMX172:
                        minGain = 8;
                        maxGain = 48;
                        minExposureTime = 10;
                        maxExposureTime = 2990;
                        break;
                }

                // AE done
                if ((m_ImageMean > dTargetMean * 0.8 && m_ImageMean < dTargetMean * 1.2)
                    || ((m_curExpXGain >= maxExposureTime * maxGain) && (m_ImageMean < dTargetMean * 0.8))
                    || ((m_curExpXGain <= minExposureTime * minGain) && (m_ImageMean > dTargetMean * 1.2)))
                {
                    m_AE_done = true;
                    return;
                }

                m_AE_done = false;
                int calExpXGain = (int)(m_curExpXGain * (dTargetMean / m_ImageMean));
                m_curExpXGain = (m_curExpXGain + calExpXGain) / 2;

                m_PrevImageMean = m_ImageMean;

                if (m_curExpXGain >= maxExposureTime * minGain)
                {
                    m_curExpTimeInLines = maxExposureTime;
                    m_curGain = (int)(m_curGain * (dTargetMean / m_ImageMean));
                    if (m_curGain > maxGain)
                    {
                        m_curGain = maxGain;
                        //m_AE_done = true;
                    }
                    else if (m_curGain < minGain)
                    {
                        m_curGain = minGain;
                        //m_AE_done = false;
                    }
                }
                else if (m_curExpXGain < minExposureTime * minGain)
                {
                    m_curExpTimeInLines = minExposureTime;
                    m_curGain = minGain;
                    //m_AE_done = true;
                }
                else
                {
                    m_curGain = minGain;
                    m_curExpTimeInLines = (int)(m_curExpTimeInLines * (dTargetMean / m_ImageMean));
                    //m_AE_done = false;
                }

                m_curExpXGain = m_curExpTimeInLines * m_curGain;
                capture.ExposureExt = m_curExpTimeInLines;
                capture.Gain = m_curGain;
                frmRegRW_MODESET.ExpTime = m_curExpTimeInLines;
            }

        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            thread.Abort(); // YKB 20180423 add 退出线程
            thread.Join();

            Application.Exit();
        }

        private void updateDeviceInfo()
        {
            if (capture != null)
            {
                uUIDToolStripMenuItem.Text = "UUID: " + CameraUUID.ToString();
                firmwareRevToolStripMenuItem.Text = "Firmware Rev: " + FwRev.ToString();
                hardwareRevToolStripMenuItem.Text = "Hardware Rev: " + (HwRev & 0x0FFF).ToString("X4");
                dataFormatToolStripMenuItem.Text = "Data Format: " + m_SensorDataMode.ToString();
                devInfoToolStripMenuItem.Enabled = true;

                toolStripStatusLabelDevice.Text = capture.cameraList[m_CameraIndex].Name.ToString();
                toolStripStatusLabelHWRev.Text = (HwRev & 0x0FFF).ToString("X4");
                toolStripStatusLabelFWRev.Text = FwRev.ToString();
                toolStripStatusLabelRes.Text = capture.ResList[m_ResolutionIndex, 0].ToString() 
                                                + "X" + capture.ResList[m_ResolutionIndex, 1].ToString();
            }
            else
            {
                toolStripStatusLabelDevice.Text = "None";
                toolStripStatusLabelHWRev.Text = "0";
                toolStripStatusLabelFWRev.Text = "0";
                toolStripStatusLabelRes.Text = "";
                devInfoToolStripMenuItem.Enabled = false;
            }
        }

        private void updateDeviceResolution()
        {
            resolutionToolStripMenuItem.DropDown.Items.Clear();

            if (capture != null)
            {
                for (int i = 0; i < capture.ResCount; i++)
                {
                    ToolStripMenuItem NEW;
                    NEW = new ToolStripMenuItem(capture.ResList[i, 0].ToString() + "X" + capture.ResList[i, 1].ToString());
                    NEW.Text = capture.ResList[i, 0].ToString() + "X" + capture.ResList[i, 1].ToString();
                    NEW.Click += new EventHandler(resolutionStripMenuItemClick);
                    NEW.CheckOnClick = true;
                    resolutionToolStripMenuItem.DropDown.Items.Add(NEW);
                }

                // check the one that is being used
                ToolStripMenuItem item = (ToolStripMenuItem)resolutionToolStripMenuItem.DropDownItems[m_ResolutionIndex];
                item.Checked = true;
            }
        }
        private void updateDeviceFrameRate()
        {
            framerateToolStripMenuItem.DropDown.Items.Clear();

            if (capture != null)
            {
                for (int i = 0; i < capture.FrameRateCNT; i++)
                {
                    ToolStripMenuItem NEW;
                    long tmp = capture.FrameRateList[m_ResolutionIndex, i];
                    long a = 10000000 / tmp;
                    NEW = new ToolStripMenuItem(a.ToString() + "fps");
                    NEW.Text = a.ToString() + "fps";
                    NEW.Click += new EventHandler(framerateToolStripMenuItemClick);
                    NEW.CheckOnClick = true;
                    framerateToolStripMenuItem.DropDown.Items.Add(NEW);
                }

                // check the one that is being used
                ToolStripMenuItem item = (ToolStripMenuItem)framerateToolStripMenuItem.DropDownItems[m_FrameRateIndex];
                item.Checked = true;
            }
        }
        private void setupMenuAndInit(int width, int height) // YKB 20180420 设置菜单属性
        {
            ToolStripMenuItem item;

            if (capture != null)
            {
                if (capture.cameraModel == LPCamera.CameraModel.V034
                        || capture.cameraModel == LPCamera.CameraModel.M031
                        || capture.cameraModel == LPCamera.CameraModel.MT9P031
                        || capture.cameraModel == LPCamera.CameraModel.AR0330
                        || capture.cameraModel == LPCamera.CameraModel.Stereo
                        || capture.cameraModel == LPCamera.CameraModel.C570
                        || capture.cameraModel == LPCamera.CameraModel.C661
                        || capture.cameraModel == LPCamera.CameraModel.ICX285
                        || capture.cameraModel == LPCamera.CameraModel.AR1820 
                        || capture.cameraModel == LPCamera.CameraModel.IMX22x
                        || capture.cameraModel == LPCamera.CameraModel.ov10640
                        || capture.cameraModel == LPCamera.CameraModel.OV8865
                        || capture.cameraModel == LPCamera.CameraModel.OV13850
                        || capture.cameraModel == LPCamera.CameraModel.CMV300
                        || capture.cameraModel == LPCamera.CameraModel.OV7251
                        || capture.cameraModel == LPCamera.CameraModel.IMX226
                        || capture.cameraModel == LPCamera.CameraModel.OV10823
                        || capture.cameraModel == LPCamera.CameraModel.IMX172
                        || capture.cameraModel == LPCamera.CameraModel.MLX75411
						|| capture.cameraModel == LPCamera.CameraModel.KEURIG_SPI                        
                        || capture.cameraModel == LPCamera.CameraModel.ETRON3D)

                {
                    capture.SetParam(width, height, false, IntPtr.Zero);

                    captureImageToolStripMenuItem.Enabled = true;
                    triggerModeToolStripMenuItem.Enabled = true;
                    noDisplayToolStripMenuItem.Enabled = true;
                    softTriggerToolStripMenuItem.Enabled = false;
                    autoTriggerToolStripMenuItem.Enabled = false;
                    pixelOrderToolStripMenuItem.Enabled = true;
                    

                    // set initial exposure time to 500 lines
                    m_curExpTimeInLines = 500;
                    m_curGain = 8;

                    switch (capture.cameraModel)
                    {
                        case LPCamera.CameraModel.AR1820:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.GBRG;

                            item = (ToolStripMenuItem)bGGRToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.V034:
                            m_MonoSensor = true;
                            m_curExpTimeInLines = 100;
                            break;
                        case LPCamera.CameraModel.M031:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.GBRG;

                            item = (ToolStripMenuItem)gBRGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.MLX75411:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.BGGR;

                            item = (ToolStripMenuItem)gBRGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.MT9P031:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.BGGR;

                            item = (ToolStripMenuItem)bGGRToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.AR0330:
                            m_MonoSensor = true;
                            break;
                        case LPCamera.CameraModel.Stereo:
                            m_MonoSensor = true;
                            break;
                        case LPCamera.CameraModel.C570:
                            m_MonoSensor = true;
                            break;
                        case LPCamera.CameraModel.CMV300:
                            m_MonoSensor = true;
                            break;
                        case LPCamera.CameraModel.C661:
                            m_curExpTimeInLines = 32;
                            m_MonoSensor = true;
                            break;	
                        case LPCamera.CameraModel.ICX285:
                            m_MonoSensor = true;
                            m_curExpTimeInLines = 500;
                            m_curGain = 1;
                            break;
                        case LPCamera.CameraModel.IMX22x:                        
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.GRBG;
                            break;
                        case LPCamera.CameraModel.ov10640:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.RGBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.OV8865:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.RGBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.OV13850:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.RGBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.OV7251:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.RGBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.IMX226:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.RGBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;
                            break;
                        case LPCamera.CameraModel.OV10823:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.RGBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;

                            m_curExpTimeInLines = 5000;
                            m_curGain = 32;
                            break;
                        case LPCamera.CameraModel.IMX172:
                            m_MonoSensor = false;
                            m_pixelOrder = PIXEL_ORDER.GRBG;
                            item = (ToolStripMenuItem)rGBGToolStripMenuItem;
                            item.Checked = true;

                            m_curExpTimeInLines = 2000;
                            m_curGain = 16;

                            frmRegRW_MODESET.ROI_StartX = 66;
                            frmRegRW_MODESET.ROI_StartY = 10;
                            frmRegRW_MODESET.Show(); // pop up reg window to adjust X,Y offset

                            // put the window to up right corner
                            int xLocation = Screen.FromControl(this).Bounds.Width - frmRegRW_MODESET.Width;
                            frmRegRW_MODESET.Location = new Point(xLocation, 0);
                            break;
                    }

                    if (m_MonoSensor)
                    {
                        item = (ToolStripMenuItem)monoSensorToolStripMenuItem;
                        item.Checked = true;

                        pixelOrderToolStripMenuItem.Enabled = false;
                    }

                    // default emgu demo status is disabled
                    item = (ToolStripMenuItem)disableDemoToolStripMenuItem;
                    item.Checked = true;

                    capture.ExposureExt = m_curExpTimeInLines;
                    frmRegRW_MODESET.ExpTime = m_curExpTimeInLines;

                    capture.Gain = m_curGain;

                    m_curExpXGain = m_curExpTimeInLines * m_curGain;
                    m_PrevImageMean = 0;

                }
                else // YUV sensor
                {
                    capture.SetParam(width, height, true, pictBDisplay.Handle);

                    item = (ToolStripMenuItem)triggerModeToolStripMenuItem;
                    item.Checked = false;

                    item = (ToolStripMenuItem)monoSensorToolStripMenuItem;
                    item.Checked = false;

                    captureImageToolStripMenuItem.Enabled = true;
                    triggerModeToolStripMenuItem.Enabled = true;
                    noDisplayToolStripMenuItem.Enabled = false;
                    softTriggerToolStripMenuItem.Enabled = true;
                    autoTriggerToolStripMenuItem.Enabled = true;
                    pixelOrderToolStripMenuItem.Enabled = false;
                    monoSensorToolStripMenuItem.Enabled = false;
                    setTriggerDelayToolStripMenuItem.Enabled = true;
                    autoExposureSoftwareToolStripMenuItem.Enabled = false; // org code
                    //autoExposureSoftwareToolStripMenuItem.Enabled = true; // YKB 20180421 modify 打开自动曝光，默认关闭的

                    // sensor YUV mode disable emgu demo tool
                    EmguDemoToolStripMenuItem.Enabled = false;

                }

            }
        }

        private static Mutex OpenCameraMutex = new Mutex();
        private void openCameraByIndex(int index, int modeIndex) // YKB 20180420 通过索引打开相机
        {
            int width, height;

            OpenCameraMutex.WaitOne();

            try
            {
                cameraPropertyToolStripMenuItem.Enabled = false;
                optionsToolStripMenuItem.Enabled = false;
                resolutionToolStripMenuItem.Enabled = false;
                framerateToolStripMenuItem.Enabled = false;

                CloseCamera();

                capture = new LPCamera();
                capture.Open(capture.cameraList[index], m_ResolutionIndex, m_FrameRateIndex);

                CameraUUID = "";
                FuseID = "";
                HwRev = 0;
                FwRev = 0;
                MarkEn = false; 

                try // YKB 20180420 相机信息获取
                {
                    System.Threading.Thread.Sleep(500);
                    try
                    {
                        capture.ReadCamUUIDnHWFWRev(out CameraUUID, out HwRev, out FwRev);
                    }
                    catch
                    {
                        capture.ReadCamUUIDnHWFWRev(out CameraUUID, out HwRev, out FwRev, out FuseID);
                        MarkEn = true; 
                    }
                    capture.ReadExtensionINFO(out ROIX_MAX, out ROIX_MIN, out ROIY_MAX, out ROIY_MIN);
                    frmRegRW_MODESET.Update_TrackBarMinMax(ROIX_MAX,ROIX_MIN,ROIY_MAX,ROIY_MIN);

                }
                catch
                {
                }

                width = capture.ResList[modeIndex, 0];
                height = capture.ResList[modeIndex, 1];

                capture.m_capture.ReceivedOneFrame += new FrameReceivedEventHandler(onReceivedOneFrame);
                // Position video window in client rect of owner window
                pictBDisplay.Resize += new EventHandler(onPreviewWindowResize);
                
                pictureBoxCenter.Parent = pictBDisplay;
                pictureBoxTopLeft.Parent = pictBDisplay;
                pictureBoxTopRight.Parent = pictBDisplay;
                pictureBoxBottomLeft.Parent = pictBDisplay;
                pictureBoxBottomRight.Parent = pictBDisplay;

                onPreviewWindowResize(this, null);

                setupMenuAndInit(width, height); // YKB 20180420 设置菜单

                // the first 4 bits represents the sensor mode, 0x1 : RAW 8, 0x2: RAW 10, 0x3: RAW 12, 0x4: YUY2, 0x5: RAW8_DUAL
                if ((HwRev & 0xf000) == 0x1000)
                {
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8;
                    width = width * 2;
                }
                else if ((HwRev & 0xf000) == 0x2000)
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW10;
                else if ((HwRev & 0xf000) == 0x3000)
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW12;
                else if ((HwRev & 0xf000) == 0x4000)
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV;
                else if ((HwRev & 0xf000) == 0x5000)
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL;
                else if (capture.cameraModel == LPCamera.CameraModel.ZED)
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL;

                width = width * (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL ? 2 : 1);

                // ETRON3D camera is YUY2 data format, in order to handle it and display
                // receive it as raw10 data format, added depth image display area in the right form
                if (capture.cameraModel == LPCamera.CameraModel.ETRON3D)
                {
                    m_SensorDataMode = LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW10;
                    width += 640;
                }

                this.StartPosition = FormStartPosition.Manual; // YKB 20180428 窗体的位置由Location属性决定
                this.Location = (Point)new Size(0, 0);
                this.Width = width/2 + 18; // YKB 20180428 窗口默认以图像的一半显示
                this.Height = height/2 + 50 + pictBDisplay.Top + statusStrip2.Height;
                
                //if (capture.cameraModel == CameraLP.CameraModel.M034 && width == 1280 && height == 720)
                //    setupSensorMode();
                //else
                //    cmbSensorMode.Visible = false;

                capture.Run();

                m_PrevFrameCnt = capture.FrameCount;

                cameraPropertyToolStripMenuItem.Enabled = true;
                optionsToolStripMenuItem.Enabled = true;
                resolutionToolStripMenuItem.Enabled = true;
                framerateToolStripMenuItem.Enabled = true;

                // only ar0130_ap0100 camera supports flash update
                if (capture.cameraModel == LPCamera.CameraModel.AR0130_AP0100)
                    programFlashToolStripMenuItem.Enabled = true;
                else
                    programFlashToolStripMenuItem.Enabled = false;

                cameraList = CameraType.LEOPARD_CAMERA;

                updateDeviceInfo();
                updateDeviceResolution();
                updateDeviceFrameRate();
            }
            catch
            {
            }
            
            OpenCameraMutex.ReleaseMutex();
        }

        /// <summary> Resize the preview when the PreviewWindow is resized </summary>
        protected void onPreviewWindowResize(object sender, EventArgs e)
        {
            if (capture != null)
            {
                // if window size changed, disable rect drawing to avoid confusion
                drawingRect = false;

                if (capture.rendererWin != null)
                {
                    // Position video window in client rect of owner window
                    Rectangle rc = pictBDisplay.ClientRectangle;
                    capture.rendererWin.SetWindowPosition(0, 0, rc.Right, rc.Bottom);
                    pictureBoxCenter.Height = 10;
                    pictureBoxCenter.Width = 10;
                    pictureBoxCenter.Top = (rc.Bottom - rc.Top) / 2 - 25;
                    pictureBoxCenter.Left = (rc.Right - rc.Left) / 2 - 25;

                    pictureBoxTopLeft.Height = 10;
                    pictureBoxTopLeft.Width = 10;
                    pictureBoxTopLeft.Top = (rc.Bottom - rc.Top) / 3 - 25 -25 ;
                    pictureBoxTopLeft.Left = (rc.Right - rc.Left) / 3 - 25 - 25;

                    pictureBoxTopRight.Height = 10;
                    pictureBoxTopRight.Width = 10;
                    pictureBoxTopRight.Top = (rc.Bottom - rc.Top) / 3 - 25 -25;
                    pictureBoxTopRight.Left = (rc.Right - rc.Left) * 2 / 3 - 25 + 25;

                    pictureBoxBottomLeft.Height = 10;
                    pictureBoxBottomLeft.Width = 10;
                    pictureBoxBottomLeft.Top = (rc.Bottom - rc.Top) * 2 / 3 - 25 + 25;
                    pictureBoxBottomLeft.Left = (rc.Right - rc.Left) / 3 - 25 - 25;

                    pictureBoxBottomRight.Height = 10;
                    pictureBoxBottomRight.Width = 10;
                    pictureBoxBottomRight.Top = (rc.Bottom - rc.Top) * 2 / 3 - 25 + 25;
                    pictureBoxBottomRight.Left = (rc.Right - rc.Left) * 2 / 3 - 25 + 25;
                }
            }
        }

        #region DLL
        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth,
           int nHeight, IntPtr hObjSource, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);    

        [DllImport("gdi32.dll")]
        static extern bool StretchBlt(IntPtr hdcDest, int nXOriginDest,
            int nYOriginDest, int nWidthDest, int nHeightDest, IntPtr hdcSrc,
            int nXOriginSrc, int nYOriginSrc, int nWidthSrc, int nHeightSrc,
            TernaryRasterOperations dwRop);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        static extern bool SetStretchBltMode(IntPtr hdc, StretchMode iStretchMode);

        public enum StretchMode
        {
            STRETCH_ANDSCANS = 1,
            STRETCH_ORSCANS = 2,
            STRETCH_DELETESCANS = 3,
            STRETCH_HALFTONE = 4,
        }

        public enum TernaryRasterOperations
        {
            SRCCOPY = 0x00CC0020, /* dest = source*/
            SRCPAINT = 0x00EE0086, /* dest = source OR dest*/
            SRCAND = 0x008800C6, /* dest = source AND dest*/
            SRCINVERT = 0x00660046, /* dest = source XOR dest*/
            SRCERASE = 0x00440328, /* dest = source AND (NOT dest )*/
            NOTSRCCOPY = 0x00330008, /* dest = (NOT source)*/
            NOTSRCERASE = 0x001100A6, /* dest = (NOT src) AND (NOT dest) */
            MERGECOPY = 0x00C000CA, /* dest = (source AND pattern)*/
            MERGEPAINT = 0x00BB0226, /* dest = (NOT source) OR dest*/
            PATCOPY = 0x00F00021, /* dest = pattern*/
            PATPAINT = 0x00FB0A09, /* dest = DPSnoo*/
            PATINVERT = 0x005A0049, /* dest = pattern XOR dest*/
            DSTINVERT = 0x00550009, /* dest = (NOT dest)*/
            BLACKNESS = 0x00000042, /* dest = BLACK*/
            WHITENESS = 0x00FF0062, /* dest = WHITE*/
        };

        #endregion

        private void SaveCapturedImage() // YKB 20180420 单帧保存图片
        {
            if (!m_SaveFrameToFile) // YKB 20180420 如果不需要保存则退出
            {
                return;
            }
            m_SaveFileInProcess = true; // YKB 20180420 当前帧图片保存正在进行

            string MySavePath = ".\\image\\"; // YKB 20180423 modify 修改文件路径为可执行文件的当前目录下的image
            if (!Directory.Exists(MySavePath))//判断是否存在
            {
                Directory.CreateDirectory(MySavePath);//创建新路径
            }

            string MyFileName = MySavePath + "PMH_" + DateTime.Now.ToString("yyyyMMdd_hh-mm-ss_fff");

            // 保存YUV格式图像
            // LeopardCamera.Tools.SaveRAWfile(imageArray, MyFileName);

            // 保存BMP格式图像
            string MybmpFileName = Path.ChangeExtension(MyFileName, ".bmp");         
            imageBmp.Save(MybmpFileName, System.Drawing.Imaging.ImageFormat.Bmp);

            //Modify End

            imageBmp.Dispose();

            m_SaveFrameToFile = false;
            m_SaveFileInProcess = false; // YKB 20180420 当前帧图片保存完成
        }

        private int GetEmbeddedFrameCount(IntPtr pBuffer, int width, int height, int bpp)
        {
            int len = 4 * 5;
            byte[] imageArrayFrmCount = new byte[len];

            Marshal.Copy(pBuffer, imageArrayFrmCount, 0, len);

            int frameCount = (int)(imageArrayFrmCount[5] >> 4) << 12
                            | (int)(imageArrayFrmCount[5+4] >> 4) << 8
                            | (int)(imageArrayFrmCount[5+4*2] >> 4) << 4
                            | (int)(imageArrayFrmCount[5+4*3] >> 4);

            return frameCount;
        }

        private void CopyFrame(IntPtr pBuffer, int width, int height, int bpp) // YKB 20180423 复制图像，如果保存帧到文件标志位为true，则复制
        {
            if (m_SaveFrameToFile) // YKB 20180423 如果当前处于保存图像中，则退出
                return;

            int iSize = width * height * ( (bpp - 1 ) / 8 + 1) ;
            imageArray = new byte[iSize];

            Marshal.Copy(pBuffer, imageArray, 0, iSize);

            if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
                imageBmp = LeopardCamera.Tools.ConvrtYUV422BMP(pBuffer, width, height, MarkEn, (pictureBoxCenter.Top * height / pictBDisplay.Height) * width + pictureBoxCenter.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxTopLeft.Top * height / pictBDisplay.Height) * width + pictureBoxTopLeft.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxBottomLeft.Top * height / pictBDisplay.Height) * width + pictureBoxBottomLeft.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxTopRight.Top * height / pictBDisplay.Height) * width + pictureBoxTopRight.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxBottomRight.Top * height / pictBDisplay.Height) * width + pictureBoxBottomRight.Left * width / pictBDisplay.Width);

            else
            {
                imageBmp = LeopardCamera.Tools.ConvertBayer2BMP(pBuffer, width, height, bpp, (int)m_pixelOrder, 1.6, m_MonoSensor,
                    (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL));

                if (m_Show_Anchors)
                    AddAnchorsToBmp(imageBmp);
            }

            m_SaveFrameToFile = true; // YKB 20180423 保存标志位置位，等待保存
        }

        private void CopyFrame_YKB(IntPtr pBuffer, int width, int height, int bpp) // YKB 20180509 复制图像，YUV转RGB
        {
            if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
                imageBmpSave = LeopardCamera.Tools.ConvrtYUV422BMP(pBuffer, width, height, MarkEn, (pictureBoxCenter.Top * height / pictBDisplay.Height) * width + pictureBoxCenter.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxTopLeft.Top * height / pictBDisplay.Height) * width + pictureBoxTopLeft.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxBottomLeft.Top * height / pictBDisplay.Height) * width + pictureBoxBottomLeft.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxTopRight.Top * height / pictBDisplay.Height) * width + pictureBoxTopRight.Left * width / pictBDisplay.Width,
                                                                                       (pictureBoxBottomRight.Top * height / pictBDisplay.Height) * width + pictureBoxBottomRight.Left * width / pictBDisplay.Width);
            else
            {
                imageBmpSave = LeopardCamera.Tools.ConvertBayer2BMP(pBuffer, width, height, bpp, (int)m_pixelOrder, 1.6, m_MonoSensor,
                    (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL));
            }
        }

        private void SavePreFrame(IntPtr pBuffer, int width, int height, int bpp)
        {

            int iSize = width * height * ((bpp - 1) / 8 + 1);
            imageArrayPre = new byte[iSize];

            Marshal.Copy(pBuffer, imageArrayPre, 0, iSize);
        }

        private void AddAnchorsToBmp(Bitmap bitmap)
        {
            System.Drawing.Graphics newGraphics = Graphics.FromImage(bitmap);

            SolidBrush redBrush = new SolidBrush(Color.Red);

            int box_X, box_Y, box_W=10, box_H = 10;

            box_Y = bitmap.Height / 2 - 25;
            box_X = bitmap.Width / 2 - 25;
            newGraphics.FillRectangle(redBrush, box_X, box_Y, box_W, box_H);

            box_Y = bitmap.Height / 3 - 25 - 25;
            box_X = bitmap.Width / 3 - 25 - 25;
            newGraphics.FillRectangle(redBrush, box_X, box_Y, box_W, box_H);

            box_Y = bitmap.Height / 3 - 25 - 25;
            box_X = bitmap.Width * 2 / 3 - 25 + 25;
            newGraphics.FillRectangle(redBrush, box_X, box_Y, box_W, box_H);

            box_Y = bitmap.Height * 2 / 3 - 25 + 25;
            box_X = bitmap.Width / 3 - 25 - 25;
            newGraphics.FillRectangle(redBrush, box_X, box_Y, box_W, box_H);

            box_Y = bitmap.Height * 2 / 3 - 25 + 25;
            box_X = bitmap.Width * 2 / 3 - 25 + 25;
            newGraphics.FillRectangle(redBrush, box_X, box_Y, box_W, box_H);            

        }

        private void DisplayImage(Bitmap bitmap, PictureBox pictbox)
        {
            System.Drawing.Graphics newGraphics = Graphics.FromImage(bitmap);

            if (this.pictBDisplay.Width != 0 && this.pictBDisplay.Height != 0)
            {
                if (drawingRect && m_boxRect.Width > 1 && m_boxRect.Height > 1)
                {
                    int iWidth = bitmap.Width;
                    int iHeight = bitmap.Height;

                    int box_X = m_boxRect.X * iWidth / this.pictBDisplay.Width;
                    int box_Y = m_boxRect.Y * iHeight / this.pictBDisplay.Height;
                    int box_W = m_boxRect.Width * iWidth / this.pictBDisplay.Width;
                    int box_H = m_boxRect.Height * iHeight / this.pictBDisplay.Height;

                    box_X = box_X < 0 ? 0 : box_X > iWidth ? iWidth : box_X;
                    box_Y = box_Y < 0 ? 0 : box_Y > iWidth ? iHeight : box_Y;
                    box_W = box_W < 1 ? 1 : box_W + box_X > iWidth ? iWidth - box_X : box_W;
                    box_H = box_H < 1 ? 1 : box_H + box_Y > iHeight ? iHeight - box_Y : box_H;

                    Pen pen = new Pen(Color.Red, 2);
                    newGraphics.DrawRectangle(pen, box_X, box_Y, box_W, box_H);
                }
            }

            System.Drawing.Graphics formGraphics = pictbox.CreateGraphics();

            IntPtr hbmp = bitmap.GetHbitmap();

            IntPtr pTarget = formGraphics.GetHdc();
            IntPtr pSource = CreateCompatibleDC(pTarget);
            IntPtr pOrig = SelectObject(pSource, hbmp);

            SetStretchBltMode(pTarget, StretchMode.STRETCH_DELETESCANS);

            StretchBlt(pTarget, 0,
                0, pictbox.Width, pictbox.Height, pSource,
                0, 0, bitmap.Width, bitmap.Height, TernaryRasterOperations.SRCCOPY);

            //BitBlt(pTarget, 0, 0, bitmap.Width, bitmap.Height, pSource, 0, 0, TernaryRasterOperations.SRCCOPY);

            IntPtr pNew = SelectObject(pSource, pOrig);
            DeleteObject(pNew);
            DeleteDC(pSource);
            formGraphics.ReleaseHdc(pTarget);
        }

        #region APIs
        [DllImport("Kernel32.dll", EntryPoint = "RtlMoveMemory")]
        protected static extern void CopyMemory(IntPtr Destination, IntPtr Source, [MarshalAs(UnmanagedType.U4)] int Length);
        #endregion

        private int PreEmbeddedFrmCnt = 0;
        /// <summary> capture one frame and display it </summary>
        protected void onReceivedOneFrame(object sender, EventArgs e)
        {
            IntPtr pBuffer1 = IntPtr.Zero;
            IntPtr pBufferProc = IntPtr.Zero;

            int iWidth = 0, iHeight = 0, iBPP = 0;
            int dataWidth = 8;

            capture.CaptureImageNoWait(out pBuffer1, out iWidth, out iHeight, out iBPP); // 图像数据获取

            //*************************************设备是否丢失计算***********************************************
            if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW12
                || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW10)
            {
                int embeddedFrameCount = GetEmbeddedFrameCount(pBuffer1, iWidth, iHeight, iBPP);
                if (embeddedFrameCount != PreEmbeddedFrmCnt + 1
                    && PreEmbeddedFrmCnt != 0)
                    FrameDisconntinued = true;
                else
                    FrameDisconntinued = false;

                PreEmbeddedFrmCnt = embeddedFrameCount;
            }
            //*************************************设备是否丢失计算结束***********************************************

            //*************************************数据宽度计算***********************************************
            if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8)
                iWidth = iWidth * 2;
            else if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW10)
                dataWidth = 10;
            else if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW12)
                dataWidth = 12;
            else if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL)
                dataWidth = 8;
            //*************************************数据宽度计算结束***********************************************

            //*************************************带插件的数据计算***********************************************
            // pbuffer will be changed by convert2BMP. make a copy and process
            if (m_selectedPlugin != null)
            {
                string cameraID;

                if (FuseID != "")
                    cameraID = FuseID;
                else
                    cameraID = CameraUUID;

                //if (capture.cameraModel == LPCamera.CameraModel.IMX172)
                if (iWidth > 1280 && iHeight > 720)
                {
                    pBufferProc = Marshal.AllocHGlobal(1280 * 720 * ((dataWidth - 1) / 8 + 1) * 2);
                    LeopardCamera.Tools.ReframeTo720p(pBufferProc, pBuffer1, iWidth, iHeight, dataWidth);
                    
                    if (m_AutoExposure)
                    {
                        int startx, starty;
                        int iSize = 150;

                        startx = (1280 - iSize) / 2;
                        starty = (720 - iSize) / 2;
                        m_ImageMean = LeopardCamera.Tools.CalcMean(pBufferProc, 1280, 720, startx, starty, iSize, dataWidth);
                        dTargetMean = (double)(0x01 << (dataWidth - 2)) * dTargetMeanFactor;
                    }

                    PluginProcess(pBufferProc, 1280, 720, dataWidth, m_SensorDataMode, m_MonoSensor, 
                            (int)m_pixelOrder, m_curExpTimeInLines, cameraID); 

                }
                else if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                    || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL
                    || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL)
                {
                    pBufferProc = Marshal.AllocHGlobal(iWidth * iHeight * ((dataWidth - 1) / 8 + 1) * 2);
                    CopyMemory(pBufferProc, pBuffer1, iWidth * iHeight * ((dataWidth - 1) / 8 + 1) * 2);
                    PluginProcess(pBufferProc, iWidth, iHeight, dataWidth, m_SensorDataMode, m_MonoSensor, 
                        (int)m_pixelOrder, m_curExpTimeInLines, cameraID);
                }
                else
                {
                    pBufferProc = Marshal.AllocHGlobal(iWidth * iHeight * ((dataWidth - 1) / 8 + 1));
                    CopyMemory(pBufferProc, pBuffer1, iWidth * iHeight * ((dataWidth - 1) / 8 + 1));
                    PluginProcess(pBufferProc, iWidth, iHeight, dataWidth, m_SensorDataMode, m_MonoSensor, 
                        (int)m_pixelOrder, m_curExpTimeInLines, cameraID);
                }
                Marshal.FreeHGlobal(pBufferProc);
            }
            //*************************************带插件的数据计算结束***********************************************


            if (m_AutoTrigger) // 自动曝光帧号记录，曝光时间计算
            {
                triggerEndTime = DateTime.Now;
                m_AutoTriggerCnt++;
            }

            //*************************************图像数据转换***********************************************
            if (m_SensorDataMode != LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                && m_SensorDataMode != LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
            {

                if (m_AutoExposure)
                {
                    int startx, starty;
                    int iSize = 256;

                    startx = (iWidth - iSize) / 2;
                    starty = (iHeight - iSize) / 2;
                    m_ImageMean = LeopardCamera.Tools.CalcMean(pBuffer1, iWidth, iHeight, startx, starty, iSize, dataWidth);
                    dTargetMean = (double)(0x01 << (dataWidth - 2)) * dTargetMeanFactor;
                }

                if (iWidth != 0 && iHeight != 0 && mRAWDisplay)
                {
                    Bitmap bitmap;

                    if (m_CaptureOneImage)
                    {
                        CopyFrame_YKB(pBuffer1, iWidth, iHeight, dataWidth);
                        iNumDiff = capture.FrameCount - iLastFrameCount;
                        m_SaveFrameToFile = true;
                        capture.FreeImageBuffer(pBuffer1);
                        return;
                    }

                    if (this.pictBDisplay.Width != 0 && this.pictBDisplay.Height != 0
                        && m_NoiseCalculationEna)
                    {
                        int box_X = m_boxRect.X * iWidth / this.pictBDisplay.Width;
                        int box_Y = m_boxRect.Y * iHeight / this.pictBDisplay.Height;
                        int box_W = m_boxRect.Width * iWidth / this.pictBDisplay.Width;
                        int box_H = m_boxRect.Height * iHeight / this.pictBDisplay.Height;

                        box_X = box_X < 0 ? 0 : box_X > iWidth ? iWidth : box_X;
                        box_Y = box_Y < 0 ? 0 : box_Y > iWidth ? iHeight : box_Y;
                        box_W = box_W < 1 ? 1 : box_W + box_X > iWidth ? iWidth - box_X : box_W;
                        box_H = box_H < 1 ? 1 : box_H + box_Y > iHeight ? iHeight - box_Y : box_H;

                        if (drawingRect && m_boxRect.Height != 0 && m_boxRect.Width != 0) // calculate mean & STD
                        {
                            m_RectMean = LeopardCamera.Tools.CalcMean(pBuffer1, iWidth, iHeight, box_X, box_Y,
                                                                        box_W, box_H, dataWidth);
                            m_RectSTD = LeopardCamera.Tools.CalcSTD(pBuffer1, m_RectMean, iWidth, iHeight, box_X, box_Y,
                                                                        box_W, box_H, dataWidth);
                            //m_RectTN = LeopardCamera.Tools.CalcTemporalNoise(pBuffer1, imageArrayPre, iWidth, iHeight, box_X, box_Y,
                            //                                            box_W, box_H, dataWidth);
                            //m_RectFPN = m_RectSTD - m_RectTN;
                        }
                        else
                        {
                            m_RectMean = LeopardCamera.Tools.CalcMean(pBuffer1, iWidth, iHeight, box_X, box_Y,
                                                                        1, 1, dataWidth);
                            m_RectSTD = 0;
                            m_RectFPN = 0;
                            m_RectTN = 0;
                        }

                        SavePreFrame(pBuffer1, iWidth, iHeight, dataWidth);
                    }

                    if (capture.cameraModel == LPCamera.CameraModel.ETRON3D)
                    {
                        bitmap = LeopardCamera.Tools.ConvrtYUV422BMP(pBuffer1, iWidth + 640, iHeight, false, 0, 0, 0, 0, 0);
                    }
                    else
                    {
                         bitmap = LeopardCamera.Tools.ConvertBayer2BMP(pBuffer1, iWidth, iHeight, dataWidth, (int)m_pixelOrder, 1.6, m_MonoSensor, 
                                        (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.RAW8_DUAL));
                    }  

                    // emguCV demo
                    bitmap = EmguTool.EmguDemo.EmguDemoRun(m_EmguDemoId, bitmap);

                    if (m_Show_Anchors)
                    {
                       AddAnchorsToBmp(bitmap);
                    }

                    DisplayImage(bitmap, pictBDisplay);

                    //DisplayImage(bitmap, pictureDisplayColor);
                    bitmap.Dispose();
                }
            }
            else // YUV
            {

                //*************************************用于保存的图像获取***********************************************
                if (m_SaveAllImage) // YKB 20180509 多帧图像获取
                {
                    if (!m_SaveFrameToFile)
                    {
                        CopyFrame_YKB(pBuffer1, iWidth, iHeight, dataWidth); // 将BYTE数据转换成bitmap数据imageBmpSave

                        iNumDiff = capture.FrameCount - iLastFrameCount;
                        m_SaveFrameToFile = true; // YKB 20180509 图像获取完成，保存标志位使能，等待保存
                    }
                }
                if (m_CaptureOneImage) // YKB 20180509 单帧图像获取
                {
                    if (!m_SaveFrameToFile)
                    {
                        CopyFrame_YKB(pBuffer1, iWidth, iHeight, dataWidth); // 将BYTE数据转换成bitmap数据imageBmpSave
                        m_SaveFrameToFile = true; // YKB 20180509 图像获取完成，保存标志位使能，等待保存
                    }
                }
                //****************************************用于保存的图像获取结束********************************************

                if (m_NoiseCalculationEna)
                {
                    LeopardCamera.Tools.yuv422_TO_y(pBuffer1, pBuffer1, iWidth, iHeight);

                    m_RectMean = LeopardCamera.Tools.CalcMean(pBuffer1, iWidth, iHeight, 0, 0,
                                                  iWidth, iHeight, dataWidth);
                    m_RectSTD = LeopardCamera.Tools.CalcSTD(pBuffer1, m_RectMean, iWidth, iHeight, 0, 0,
                                                                iWidth, iHeight, dataWidth);
                    //m_RectTN = 0.0; LeopardCamera.Tools.CalcTemporalNoise(pBuffer1, imageArrayPre, iWidth, iHeight, 0, 0,
                    //                                             iWidth, iHeight, dataWidth);
                    //m_RectFPN = m_RectSTD - m_RectTN;

                    //SavePreFrame(pBuffer1, iWidth, iHeight, dataWidth);
                }

            }
            //*************************************图像数据转换结束***********************************************

            capture.FreeImageBuffer(pBuffer1);

        }

        private void DevicesStripMenuItemClick(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        //txtNoteName.Text = item.Text;
                        item.Checked = true;

                        m_CameraIndex = (item.OwnerItem as ToolStripMenuItem).DropDownItems.IndexOf(item);
                        openCameraByIndex(m_CameraIndex, m_ResolutionIndex);
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }

        private void resolutionStripMenuItemClick(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        //txtNoteName.Text = item.Text;
                        item.Checked = true;

                        m_ResolutionIndex = (item.OwnerItem as ToolStripMenuItem).DropDownItems.IndexOf(item);
                        m_FrameRateIndex = 0;//always 0 when switching to another resolution
                        openCameraByIndex(m_CameraIndex, m_ResolutionIndex);
                        return;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }
        private void framerateToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        //txtNoteName.Text = item.Text;
                        item.Checked = true;

                        m_FrameRateIndex = (item.OwnerItem as ToolStripMenuItem).DropDownItems.IndexOf(item);
                        // capture.framerateindex_curRes = m_FrameRateIndex;

                        openCameraByIndex(m_CameraIndex, m_ResolutionIndex);
                        return;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }
        private void AddCamerasToMenu()
        {
            DevicesStripMenuItem.DropDown.Items.Clear();

            if (capture == null)
                return;

            if (capture.cameraList.Count == 0)
                return;

            for (int i = 0; i < capture.cameraList.Count; i++)
            {
                ToolStripMenuItem NEW;
                NEW = new ToolStripMenuItem(capture.cameraList[i].Name.ToString());
                NEW.Text = capture.cameraList[i].Name.ToString();
                NEW.Click += new EventHandler(DevicesStripMenuItemClick);
                NEW.CheckOnClick = true;
                DevicesStripMenuItem.DropDown.Items.Add(NEW);
            }

        }

        private void CloseCamera()
        {
            if (capture != null)
            {
                // Remove the Resize event handler
                pictBDisplay.Resize -= new EventHandler(onPreviewWindowResize);
                capture.m_capture.ReceivedOneFrame -= new FrameReceivedEventHandler(onReceivedOneFrame);

                capture.Stop();
                capture.Close();
            }
        }

        private void DetectCamera()
        {
            try
            {
                CloseCamera();
                capture = new LPCamera();
                AddCamerasToMenu();
                openCameraByIndex(m_CameraIndex, m_ResolutionIndex);

                ToolStripMenuItem item = (ToolStripMenuItem)DevicesStripMenuItem.DropDownItems[m_CameraIndex];
                item.Checked = true;
                item = (ToolStripMenuItem)resolutionToolStripMenuItem.DropDownItems[m_ResolutionIndex];
                item.Checked = true;

            }
            catch (Exception ex)
            {
                capture = null;

                CameraUUID = "";
                HwRev = 0;
                FwRev = 0;
                cameraList = CameraType.NO_CAMERA;

                AddCamerasToMenu();
                updateDeviceInfo();
                updateDeviceResolution();
                updateDeviceFrameRate();

                cameraPropertyToolStripMenuItem.Enabled = false;
                optionsToolStripMenuItem.Enabled = false;
                resolutionToolStripMenuItem.Enabled = false;
                triggerModeToolStripMenuItem.Enabled = false;
                autoTriggerToolStripMenuItem.Enabled = false;
                framerateToolStripMenuItem.Enabled = false;

                m_AutoTrigger = false;

                this.Width = 640;
                this.Height = 480;
            }

#if true // YKB 20180421 add 启动程序后即打开显示
            if (capture != null)
            {
                capture.EnableTriggerMode(false, false);
                m_TriggerMode = false;

                positiveEdgeToolStripMenuItem.Checked = false;
                autoTriggerToolStripMenuItem.Checked = false;

                //*************************************保存图像时编码设置***********************************************
                //YKB 20180503 获得PNG格式的编码器
                g_ImageCodecInfo = GetEncoderInfo("image/jpeg");
                System.Drawing.Imaging.Encoder myEncoder;
                EncoderParameter myEncoderParameter;

                // for the Quality parameter category.
                myEncoder = System.Drawing.Imaging.Encoder.Quality;
                // EncoderParameter object in the array.
                g_EncoderParameters = new EncoderParameters(1);
                //设置质量 数字越大质量越好，但是到了一定程度质量就不会增加了，MSDN上没有给范围，只说是32位非负整数
                myEncoderParameter = new EncoderParameter(myEncoder, 75L);
                g_EncoderParameters.Param[0] = myEncoderParameter;
                //*************************************保存图像时编码设置结束***********************************************

                EnableExtensionMeneItem(1);

                // YKB 20180428 增加配置文件
                //配置文件位置
                g_ConfigPath = AppDomain.CurrentDomain.BaseDirectory + "config.ini";
                //判断是否存在配置文件
                if (!File.Exists(g_ConfigPath))
                {
                    WriteProfileDefault(g_ConfigPath);
                }
                StringBuilder sb = new StringBuilder(255);
                //路径设置
                GetPrivateProfileString("Save", "SavePath", ".\\image", sb, 255, g_ConfigPath);
                g_SavePath = sb.ToString();
                GetPrivateProfileString("Save", "SaveSuffix", "", sb, 255, g_ConfigPath);
                g_SaveSuffix = sb.ToString();

                softTriggerToolStripMenuItem.Enabled = false;
                captureImageToolStripMenuItem.Enabled = true;
                autoTriggerToolStripMenuItem.Enabled = false;
                saveAllImageToolStripMenuItem.Enabled = true;

                m_AutoTrigger = false;
            }

#endif

#if true
            //m_AutoExposure = true;
#endif

        }

        /// <summary>
        /// Windows Messages
        /// Defined in winuser.h from Windows SDK v6.1
        /// Documentation pulled from MSDN.
        /// For more look at: http://www.pinvoke.net/default.aspx/Enums/WindowsMessages.html
        /// </summary>
        public enum WM : uint
        {
            /// <summary>
            /// Notifies an application of a change to the hardware configuration of a device or the computer.
            /// </summary>
            DEVICECHANGE = 0x0219,
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_CLOSE = 0xF060;

            switch ((WM)m.Msg)
            {
                case WM.DEVICECHANGE:
                    //DetectCamera();
                    break;
            }

            if (m.Msg == WM_SYSCOMMAND)
            {
                if ((m.WParam.ToInt32() & 0xFFF0) == SC_CLOSE)
                {
                    CloseCamera();
                    if ( m_selectedPlugin != null)
                        m_selectedPlugin.Close();
                    m_selectedPlugin = null;
                }
            }

            base.WndProc(ref m);
        }

        private void cameraPropertyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (capture == null)
                throw new Exception("Camera is not initialized yet.");
            else
            {
                capture.ShowCameraProperty(this.Handle);
            }
        }

        private byte[] savedImageBuf = new byte[65536];
        private int bufIndex = 0;

        private void captureImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            byte packetLen = 0;
            byte[] imageBuf = new byte[65536];
            
            int validLen = 0;
            byte[] imageBufF;

            // capture one image using uvc extension
            if (capture.cameraModel == LPCamera.CameraModel.KEURIG_SPI)
            {
               // while (true)
                {
                    Application.DoEvents();

                    //capture.SetRegRW(0, 0x0003, 0x0000); // capture image

                    capture.SetRegRW(0, 0x0007, 0x0000); // take image

                    System.Threading.Thread.Sleep(50);

                    int CaptureLatency = capture.GetREGStatus();
                    toolStripStatusLabelFPN.Text = "Latency = " + CaptureLatency.ToString() + " ms";

                    capture.SetRegRW(0, 0x0008, 0x0000); // tranfer Image
                    {
                        byte[] imageData;
                        bufIndex = 0;
                        do
                        {
                            capture.ReadCamDefectPixelTable(out imageData);

                            // byte 33 is the valid date length in this packet
                            validLen = (int)imageData[32];
                            if (validLen != 0)
                                Array.Copy(imageData, 0, imageBuf, bufIndex, validLen);
                            else
                                break;

                            bufIndex += validLen;

                        } while (validLen != 0);
                        
                        capture.SetRegRW(0, 0x0004, 0x0000); // end

                        imageBufF = new byte[bufIndex];
                        Array.Copy(imageBuf, 0, imageBufF, 0, bufIndex);
                        Array.Copy(imageBuf, 0, savedImageBuf, 0, bufIndex);

                        try
                        {
                            MemoryStream ms = new MemoryStream(imageBufF);
                            Image image = System.Drawing.Image.FromStream(ms);

                            Bitmap bitmap = new Bitmap(image);
                            //pictBDisplay.BackgroundImage = bitmap;
                            DisplayImage(bitmap, pictBDisplay);
                            bitmap.Dispose();
                            statusToolStripStatusLabel.Text = "Image size = " + bufIndex.ToString();
                        }
                        catch
                        {
                            statusToolStripStatusLabel.Text = "Image error";
                        }
                    } 
                }

                SaveFileDialog saveFileDialog1 = new SaveFileDialog();

                saveFileDialog1.Filter = "JPG files|*.jpg|All files (*.*)|*.*";

                saveFileDialog1.FilterIndex = 1;
                saveFileDialog1.RestoreDirectory = true;
                saveFileDialog1.FileName = CameraUUID + ".jpg";

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllBytes(saveFileDialog1.FileName, imageBufF);
                }

            }
            else
            {
                m_SaveFrameToFile = false; // 准备图像采集
                m_CaptureOneImage = true; // 单帧存储使能
            }
                
        }


        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SofwareRevision rev = new SofwareRevision();

            string message =
                "Camera Tool for Leopard Imaging USB3.0 Cameras\n"
                + "Revision " + rev.revision.ToString() + "\n"
                + "Leopard Imaging, Inc. 2015\n"
                + "Maintained by CIDI_YKB";
            const string caption = "About";
            var result = MessageBox.Show(message, caption);

        }

        private void noDisplayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mRAWDisplay)
            {
                mRAWDisplay = false;
                ToolStripMenuItem item = (ToolStripMenuItem)noDisplayToolStripMenuItem;
                item.Checked = true;
            }
            else
            {
                mRAWDisplay = true;
                ToolStripMenuItem item = (ToolStripMenuItem)noDisplayToolStripMenuItem;
                item.Checked = false;
            }
        }
/*
        private void triggerModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (capture != null)
            {
                if (m_TriggerMode)
                {
                    capture.EnableTriggerMode(false);
                    m_TriggerMode = false;
                    ToolStripMenuItem item = (ToolStripMenuItem)triggerModeToolStripMenuItem;
                    item.Checked = false;

                    item = (ToolStripMenuItem)autoTriggerToolStripMenuItem;
                    item.Checked = false;

                    softTriggerToolStripMenuItem.Enabled = false;
                    captureImageToolStripMenuItem.Enabled = true;
                    autoTriggerToolStripMenuItem.Enabled = false;
                    m_AutoTrigger = false;
                }
                else
                {
                    capture.EnableTriggerMode(true);
                    m_TriggerMode = true;

                    ToolStripMenuItem item = (ToolStripMenuItem)triggerModeToolStripMenuItem;
                    item.Checked = true;

                    softTriggerToolStripMenuItem.Enabled = true;
                    captureImageToolStripMenuItem.Enabled = false;
                    autoTriggerToolStripMenuItem.Enabled = true;
                }
            }
        }
*/
        private void softTriggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (capture != null)
            {
                if (m_TriggerMode)
                {
                    // set exposure & gain before trigger
                    //capture.ExposureExt = 100;
                    //capture.Gain = 1;

                    capture.SoftTrigger();
                    m_CaptureOneImage = true;
                }

            }
        }

        private void monoSensorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (capture != null)
            {
                if (m_MonoSensor)
                {
                    m_MonoSensor = false;
                    ToolStripMenuItem item = (ToolStripMenuItem)monoSensorToolStripMenuItem;
                    item.Checked = false;

                    pixelOrderToolStripMenuItem.Enabled = true;
                }
                else
                {
                    m_MonoSensor = true;
                    ToolStripMenuItem item = (ToolStripMenuItem)monoSensorToolStripMenuItem;
                    item.Checked = true;

                    pixelOrderToolStripMenuItem.Enabled = false;
                }
            }
        }
        private void gBRGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_pixelOrder = PIXEL_ORDER.GBRG;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }

        }

        private void gRBGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_pixelOrder = PIXEL_ORDER.GRBG;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }

        private void bGGRToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_pixelOrder = PIXEL_ORDER.BGGR;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }

        private void rGBGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_pixelOrder = PIXEL_ORDER.RGBG;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }

        private void autoTriggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_AutoTrigger)
            {
                m_AutoTrigger = false;
                softTriggerToolStripMenuItem.Enabled = true;

                ToolStripMenuItem item = (ToolStripMenuItem)autoTriggerToolStripMenuItem;
                item.Checked = false;
            }
            else
            {
                m_AutoTrigger = true;
                softTriggerToolStripMenuItem.Enabled = false;

                ToolStripMenuItem item = (ToolStripMenuItem)autoTriggerToolStripMenuItem;
                item.Checked = true;

                capture.SoftTrigger();
            }
        }

        private void showAnchorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_Show_Anchors)
            {
                m_Show_Anchors = false;

                ToolStripMenuItem item = (ToolStripMenuItem)showAnchorsToolStripMenuItem;
                item.Checked = false;

                pictureBoxCenter.Visible = false;
                pictureBoxTopLeft.Visible = false;
                pictureBoxTopRight.Visible = false;
                pictureBoxBottomLeft.Visible = false;
                pictureBoxBottomRight.Visible = false;
            }
            else
            {
                m_Show_Anchors = true;

                ToolStripMenuItem item = (ToolStripMenuItem)showAnchorsToolStripMenuItem;
                item.Checked = true;

                if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                    || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
                {
                    pictureBoxCenter.Visible = true;
                    pictureBoxTopLeft.Visible = true;
                    pictureBoxTopRight.Visible = true;
                    pictureBoxBottomLeft.Visible = true;
                    pictureBoxBottomRight.Visible = true;
                }
            }
        }

        private void regRWModeSETToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmRegRW_MODESET.Show();
        }

        public static ICollection<LeopardPlugin> LoadPlugins(string path)
        {
            string[] dllFileNames = null;

            if (Directory.Exists(path))
            {
                dllFileNames = Directory.GetFiles(path, "*.dll");

                ICollection<Assembly> assemblies = new List<Assembly>(dllFileNames.Length);
                foreach (string dllFile in dllFileNames)
                {
                    AssemblyName an = AssemblyName.GetAssemblyName(dllFile);
                    Assembly assembly = Assembly.Load(an);
                    assemblies.Add(assembly);
                }

                Type pluginType = typeof(LeopardPlugin);
                ICollection<Type> pluginTypes = new List<Type>();
                foreach (Assembly assembly in assemblies)
                {
                    if (assembly != null)
                    {
                        Type[] types = assembly.GetTypes();

                        foreach (Type type in types)
                        {
                            if (type.IsInterface || type.IsAbstract)
                            {
                                continue;
                            }
                            else
                            {
                                if (type.GetInterface(pluginType.FullName) != null)
                                {
                                    pluginTypes.Add(type);
                                }
                            }
                        }
                    }
                }

                ICollection<LeopardPlugin> plugins = new List<LeopardPlugin>(pluginTypes.Count);
                foreach (Type type in pluginTypes)
                {
                    LeopardPlugin plugin = (LeopardPlugin)Activator.CreateInstance(type);
                    plugins.Add(plugin);
                }

                return plugins;
            }

            return null;
        }

        private void fontDemoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_EmguDemoId = EmguTool.EmguDemo.EmguDemoId.FontDemo;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }

        }

        private void fDDemoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_EmguDemoId = EmguTool.EmguDemo.EmguDemoId.FDDemo;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }

        private void disableDemoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem)  //Check On Click.
            {
                foreach (ToolStripMenuItem item in (((ToolStripMenuItem)sender).GetCurrentParent().Items))
                {
                    if (item == sender)
                    {
                        item.Checked = true;
                        m_EmguDemoId = EmguTool.EmguDemo.EmguDemoId.DisableDemo;
                    }
                    if ((item != null) && (item != sender))
                        item.Checked = false;
                }
            }
        }

        private void setTriggerDelayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            frmSetTriggerDelay.Show();
        }

        private void handleTriggerDelayTime()
        {
            if (frmSetTriggerDelay.DelayTime != m_delayTime)
            {
                m_delayTime = frmSetTriggerDelay.DelayTime;
                if (capture != null)
                    capture.SetTriggerDelayTime((uint)m_delayTime);
            }
        }

        private void handleRegRW_MODESET()
        {
            //only active when Write or Read btn is clicked
            if (frmRegRW_MODESET.Write_Triggered)
            {
                if ((frmRegRW_MODESET.WReg_Address != W_address) || (frmRegRW_MODESET.WReg_Value != W_value)) // YKB 20180421 modify 增加写判断，地址和值都未变化时不写入，只更改标志位
                {
                    W_address = frmRegRW_MODESET.WReg_Address;
                    W_value = frmRegRW_MODESET.WReg_Value;

                    capture.SetRegRW(1, W_address, W_value);
                }
                frmRegRW_MODESET.Write_Triggered = false;
            }
            if (frmRegRW_MODESET.Read_Triggered)
            {

                frmRegRW_MODESET.Read_Triggered = false;

                R_address = frmRegRW_MODESET.RReg_Address;

                capture.SetRegRW(0, R_address, R_value);
                //capture.SetRegRW(0, R_address, R_value);

                //System.Threading.Thread.Sleep(500);
                DateTime tempTime = DateTime.Now;
                while (tempTime.AddMilliseconds(500).CompareTo(DateTime.Now) > 0)
                    Application.DoEvents();

                R_value = frmRegRW_MODESET.RReg_Value = capture.GetREGStatus();

                frmRegRW_MODESET.Update_R_Value(R_value);

                //MessageBox.Show("read back:0x" + R_value.ToString("x"));
                frmRegRW_MODESET.Read_Triggered = false;		 
            }

            if (SensorMode != frmRegRW_MODESET.sensormode)
            {
                SensorMode = frmRegRW_MODESET.sensormode;
                //MessageBox.Show("SensorMode:" + SensorMode.ToString());
                capture.SetSensorMode(SensorMode);
            }
            if (Roi_Level != frmRegRW_MODESET.roi_level)
            {
                Roi_Level = frmRegRW_MODESET.roi_level;
                //MessageBox.Show("SensorMode:" + SensorMode.ToString());
                capture.SetROI_Level(Roi_Level);
            }
            if ((ROI_StartX != frmRegRW_MODESET.ROI_StartX) || (ROI_StartY != frmRegRW_MODESET.ROI_StartY))
            {
                ROI_StartX = frmRegRW_MODESET.ROI_StartX;
                ROI_StartY = frmRegRW_MODESET.ROI_StartY;
                capture.SetPOS(ROI_StartX, ROI_StartY);
            }
        }

        private void positiveEdgeToolStripMenuItem_Click(object sender, EventArgs e)
        {

            ToolStripMenuItem item1 = (ToolStripMenuItem)negativeEdgeToolStripMenuItem;

            if (capture != null)
            {
                if (m_TriggerMode)
                {
                    if (!item1.Checked)
                    {
                        capture.EnableTriggerMode(false, false);
                        m_TriggerMode = false;
                        ToolStripMenuItem item = (ToolStripMenuItem)positiveEdgeToolStripMenuItem;
                        item.Checked = false;

                        item = (ToolStripMenuItem)autoTriggerToolStripMenuItem;
                        item.Checked = false;

                        softTriggerToolStripMenuItem.Enabled = false;
                        captureImageToolStripMenuItem.Enabled = true;
                        autoTriggerToolStripMenuItem.Enabled = false;
                        //saveAllImageToolStripMenuItem.Enabled = true; // YKB 20180421 add 打开上升沿触发时使能保存按钮，其实此时不应该处理
                        m_AutoTrigger = false;
                    }
                }
                else
                {
                    capture.EnableTriggerMode(true,true);//positive edge
                    m_TriggerMode = true;

                    ToolStripMenuItem item = (ToolStripMenuItem)positiveEdgeToolStripMenuItem;
                    item.Checked = true;
                    item1.Checked = false;

                    softTriggerToolStripMenuItem.Enabled = true;
                    captureImageToolStripMenuItem.Enabled = false;
                    autoTriggerToolStripMenuItem.Enabled = true;
                    //saveAllImageToolStripMenuItem.Enabled = false; // YKB 20180421 add 关闭上升沿触发时禁止保存按钮，其实此时不应该处理
                }
            }
        }

        private void negativeEdgeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item1 = (ToolStripMenuItem)positiveEdgeToolStripMenuItem;

            if (capture != null)
            {
                if (m_TriggerMode)
                {
                    if(!item1.Checked)
                    {
                    capture.EnableTriggerMode(false,false);
                    m_TriggerMode = false;
                    ToolStripMenuItem item = (ToolStripMenuItem)negativeEdgeToolStripMenuItem;
                    item.Checked = false;

                    item = (ToolStripMenuItem)autoTriggerToolStripMenuItem;
                    item.Checked = false;

                    softTriggerToolStripMenuItem.Enabled = false;
                    captureImageToolStripMenuItem.Enabled = true;
                    autoTriggerToolStripMenuItem.Enabled = false;
                    //saveAllImageToolStripMenuItem.Enabled = true; // YKB 20180421 add 打开下降沿触发时使能保存按钮，其实此时不应该处理
                        m_AutoTrigger = false;
                    }
                }
                else
                {
                    capture.EnableTriggerMode(true,false);//negative edge
                    m_TriggerMode = true;

                    ToolStripMenuItem item = (ToolStripMenuItem)negativeEdgeToolStripMenuItem;
                   
                    item.Checked = true;
                    item1.Checked = false;

                    softTriggerToolStripMenuItem.Enabled = true;
                    captureImageToolStripMenuItem.Enabled = false;
                    autoTriggerToolStripMenuItem.Enabled = true;
                    //saveAllImageToolStripMenuItem.Enabled = false; // YKB 20180421 add 关闭下降沿触发时禁止保存按钮，其实此时不应该处理
                }
            }
        }

        private void cameraPropWinToolStripMenuItem_Click(object sender, EventArgs e) // YKB 20180420 获取参数范围修正越界范围
        {
            if (capture != null)
            {
                CameraProperty cGain = new CameraProperty();
                CameraProperty cExposure = new CameraProperty();

                capture.GetVideoProcAmpPropertyRange(DirectShowLib.VideoProcAmpProperty.Gain, out cGain.Min, out cGain.Max, out cGain.Step, out cGain.Default);
                if (capture.Gain < cGain.Min)
                    capture.Gain = cGain.Min;
                else if (capture.Gain > cGain.Max)
                    capture.Gain = cGain.Max;

                cGain.curValue = capture.Gain;

                // YUV cameras have AE information on it
                if (m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                    || m_SensorDataMode == LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
                    m_curAE = capture.AE;
                else // non YUV cameras don't store AE information, so we restore it from m_AutoExposure
                    m_curAE = m_AutoExposure;

                capture.GetCameraControlPropertyRange(DirectShowLib.CameraControlProperty.Exposure, out cExposure.Min, out cExposure.Max, out cExposure.Step, out cExposure.Default);
                if (capture.Exposure < cExposure.Min)
                    capture.Exposure = cExposure.Min;
                else if (capture.Exposure > cExposure.Max)
                    capture.Exposure = cExposure.Max;

                cExposure.curValue = capture.Exposure;
                m_curExp = cExposure.curValue;

                frmCameraPropWin.AE = m_curAE;
                frmCameraPropWin.UpdateValue(cGain, m_curAE, cExposure, m_curExpTimeInLines);
            }
            frmCameraPropWin.Show();
        }

        private void handleCameraPropWin() // YKB 20180420 相机属性窗口
        {
            // update current gain from CameraPropWin
            if (m_curGain != frmCameraPropWin.Gain.curValue && !m_curAE)
            {
                capture.Gain = frmCameraPropWin.Gain.curValue;
                m_curGain = frmCameraPropWin.Gain.curValue;
            }

            // update current AE mode from CameraPropWin
            if (m_curAE != frmCameraPropWin.AE)
            {
                capture.AE = frmCameraPropWin.AE;
                m_curAE = frmCameraPropWin.AE;

                if (!m_curAE) // when it comes back to manual mode, set the parameters
                {
                    capture.Gain = frmCameraPropWin.Gain.curValue;
                    m_curGain = frmCameraPropWin.Gain.curValue;

                    capture.Exposure = frmCameraPropWin.Exposure.curValue;
                    m_curExp = frmCameraPropWin.Exposure.curValue;

                    m_curExpTimeInLines = frmCameraPropWin.ExpTime;
                    capture.ExposureExt = m_curExpTimeInLines;
                }
            }

            // to enable/disable auto exposure in software if it is not YUV 
            if (m_SensorDataMode != LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV
                && m_SensorDataMode != LeopardCamera.LPCamera.SENSOR_DATA_MODE.YUV_DUAL)
                m_AutoExposure = m_curAE;

            // update current expsoure from CameraPropWin
            if (m_curExp != frmCameraPropWin.Exposure.curValue && !m_curAE)
            {
                capture.Exposure = frmCameraPropWin.Exposure.curValue;
                m_curExp = frmCameraPropWin.Exposure.curValue;
            }

            // update current expsoure time ( lines) from CameraPropWin
            if (frmCameraPropWin.ExpTime != m_curExpTimeInLines && !m_curAE)
            {
                m_curExpTimeInLines = frmCameraPropWin.ExpTime;
                capture.ExposureExt = m_curExpTimeInLines;
            }
        }

        private Rectangle m_boxRect = new Rectangle( 0, 0, 0, 0 );
        private bool drawingRect = false, endofRect = false;
        private void pictBDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            toolStripStatusLabelPos.Text = e.Location.X + ":" + e.Location.Y + "." + pictBDisplay.Width.ToString() + ":" + pictBDisplay.Height.ToString();

            if (drawingRect && !endofRect)
            {
                if (e.Location.X > m_boxRect.X)
                {
                    m_boxRect.Width = e.Location.X - m_boxRect.X;
                }
                else
                {
                    m_boxRect.Width = m_boxRect.X - e.Location.X;
                    m_boxRect.X = e.Location.X;
                }

                if (e.Location.Y > m_boxRect.Y)
                {
                    m_boxRect.Height = e.Location.Y - m_boxRect.Y;
                }
                else
                {
                    m_boxRect.Height = m_boxRect.Y - e.Location.Y;
                    m_boxRect.Y = e.Location.Y;
                }
            }
            else if (m_boxRect.Width == 1 && m_boxRect.Height == 1)
            {
                m_boxRect.X = e.Location.X;
                m_boxRect.Y = e.Location.Y;
            }
        }

        private void pictBDisplay_MouseDown(object sender, MouseEventArgs e)
        {
            drawingRect = true;
            m_boxRect.X = e.Location.X;
            m_boxRect.Y = e.Location.Y;
            m_boxRect.Width = 1;
            m_boxRect.Height = 1;
        }

        private void pictBDisplay_MouseUp(object sender, MouseEventArgs e)
        {
            endofRect = true;
            if (m_boxRect.X == e.Location.X && m_boxRect.Y == e.Location.Y) // click on the same position, disable rect drawing
            {
                drawingRect = false;
                endofRect = false;
                m_boxRect.Width = 1;
                m_boxRect.Height = 1;
            }
        }


        private void parsePluginParam()
        {
            byte[] param;
            int pos = 0;

            if (capture == null)
                return;

            if (m_selectedPlugin != null)
            {
                param = m_selectedPlugin.SetParam();
                if (param != null)
                {
                    while(pos < param.Length)
                    {
                        switch ((PlugInParamType)param[pos])
                        {
                            case PlugInParamType.PI_SETGAIN:
                                pos++;
                                capture.Gain = param[pos];
                                break;
                            case PlugInParamType.PI_SETEXPOSURE:
                                pos++;
                                int exposureTime = ((int)param[pos] << 8) | (int)param[pos + 1];
                                pos+=2;
                                if (m_curExpTimeInLines != exposureTime)
                                {
                                    capture.ExposureExt = exposureTime;
                                    m_curExpTimeInLines = exposureTime;
                                }
                                break;
                            case PlugInParamType.PI_FPN:
                                pos++;
                                for (int i = 0; i < capture.Height; i++)
                                {
                                    int FPNvalue = ((int)param[pos] << 8) | (int)param[pos + 1];
                                    pos += 2;

                                    // write address to reg 5
                                    capture.SetRegRW(1, 5, i);
                                    // write data to reg 12
                                    capture.SetRegRW(1, 12, FPNvalue);
                                }

                                break;
                            default:
                                return;
                        }
                    }
                }
            }
        }

        private void CameraToolForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            thread.Abort(); // YKB 20180423 add 退出线程
            thread.Join();
            if (m_selectedPlugin != null)
            {
                m_selectedPlugin.Close();
                m_selectedPlugin = null;
            }
        }

        private void autoExposureSoftwareToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_AutoExposure)
            {
                m_AutoExposure = false;
                ToolStripMenuItem item = (ToolStripMenuItem)autoExposureSoftwareToolStripMenuItem;
                item.Checked = false;
				
				//// YKB 20180421 add 开关自动曝光（目前似乎没效果）
    //            m_curAE = false;
    //            capture.AE = m_curAE;
    //            m_AutoExposure = m_curAE;
            }
            else
            {
                m_AutoExposure = true;
                ToolStripMenuItem item = (ToolStripMenuItem)autoExposureSoftwareToolStripMenuItem;
                item.Checked = true;
				
				//// YKB 20180421 add 开关自动曝光（目前似乎没效果）
    //            m_curAE = true;
    //            capture.AE = m_curAE;
    //            m_AutoExposure = m_curAE;
            }

        }

        private void BMPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)BMPToolStripMenuItem;
            if(false == item.Checked) // 之前不是bmp存储，则重新选择文件扩展名
            {
                //*************************************保存图像时编码设置***********************************************
                //YKB 20180503 获得JPEG格式的编码器
                g_ImageCodecInfo = GetEncoderInfo("image/bmp");
                System.Drawing.Imaging.Encoder Encoder;
                EncoderParameter EncoderParameter;

                // for the Quality parameter category.
                Encoder = System.Drawing.Imaging.Encoder.Quality;
                // EncoderParameter object in the array.
                g_EncoderParameters = new EncoderParameters(1);
                //设置质量 数字越大质量越好，但是到了一定程度质量就不会增加了，MSDN上没有给范围，只说是32位非负整数
                EncoderParameter = new EncoderParameter(Encoder, 100L);
                g_EncoderParameters.Param[0] = EncoderParameter;
                //*************************************保存图像时编码设置结束***********************************************

                EnableExtensionMeneItem(0);
            }
            else
            {
            }
        }

        private void JPGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)JPGToolStripMenuItem;

            if (false == item.Checked) // 之前不是jpg存储，则重新选择文件扩展名
            {
                //*************************************保存图像时编码设置***********************************************
                //YKB 20180503 获得JPEG格式的编码器
                g_ImageCodecInfo = GetEncoderInfo("image/jpeg");
                System.Drawing.Imaging.Encoder Encoder;
                EncoderParameter EncoderParameter;

                // for the Quality parameter category.
                Encoder = System.Drawing.Imaging.Encoder.Quality;
                // EncoderParameter object in the array.
                g_EncoderParameters = new EncoderParameters(1);
                //设置质量 数字越大质量越好，但是到了一定程度质量就不会增加了，MSDN上没有给范围，只说是32位非负整数
                EncoderParameter = new EncoderParameter(Encoder, 75L);
                g_EncoderParameters.Param[0] = EncoderParameter;
                //*************************************保存图像时编码设置结束***********************************************

                EnableExtensionMeneItem(1);
            }
            else
            {
            }
        }

        private void TIFFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)TIFFToolStripMenuItem;

            if (false == item.Checked) // 之前不是gif存储，则重新选择文件扩展名
            {
                //*************************************保存图像时编码设置***********************************************
                //YKB 20180503 获得PNG格式的编码器
                g_ImageCodecInfo = GetEncoderInfo("image/tiff");
                System.Drawing.Imaging.Encoder Encoder;
                EncoderParameter EncoderParameter;

                // for the Quality parameter category.
                Encoder = System.Drawing.Imaging.Encoder.Quality;
                // EncoderParameter object in the array.
                g_EncoderParameters = new EncoderParameters(1);
                //设置质量 数字越大质量越好，但是到了一定程度质量就不会增加了，MSDN上没有给范围，只说是32位非负整数
                EncoderParameter = new EncoderParameter(Encoder, 100L);
                g_EncoderParameters.Param[0] = EncoderParameter;
                //*************************************保存图像时编码设置结束***********************************************

                EnableExtensionMeneItem(2);
            }
            else
            {
            }
        }

        private void GIFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)GIFToolStripMenuItem;

            if (false == item.Checked) // 之前不是gif存储，则重新选择文件扩展名
            {
                //*************************************保存图像时编码设置***********************************************
                //YKB 20180503 获得PNG格式的编码器
                g_ImageCodecInfo = GetEncoderInfo("image/gif");
                System.Drawing.Imaging.Encoder Encoder;
                EncoderParameter EncoderParameter;

                // for the Quality parameter category.
                Encoder = System.Drawing.Imaging.Encoder.Quality;
                // EncoderParameter object in the array.
                g_EncoderParameters = new EncoderParameters(1);
                //设置质量 数字越大质量越好，但是到了一定程度质量就不会增加了，MSDN上没有给范围，只说是32位非负整数
                EncoderParameter = new EncoderParameter(Encoder, 100L);
                g_EncoderParameters.Param[0] = EncoderParameter;
                //*************************************保存图像时编码设置结束***********************************************

                EnableExtensionMeneItem(3);
            }
            else
            {
            }
        }

        private void PNGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)PNGToolStripMenuItem;

            if (false == item.Checked) // 之前不是png存储，则重新选择文件扩展名
            {
                //*************************************保存图像时编码设置***********************************************
                //YKB 20180503 获得PNG格式的编码器
                g_ImageCodecInfo = GetEncoderInfo("image/png");
                System.Drawing.Imaging.Encoder Encoder;
                EncoderParameter EncoderParameter;

                // for the Quality parameter category.
                Encoder = System.Drawing.Imaging.Encoder.Quality;
                // EncoderParameter object in the array.
                g_EncoderParameters = new EncoderParameters(1);
                //设置质量 数字越大质量越好，但是到了一定程度质量就不会增加了，MSDN上没有给范围，只说是32位非负整数
                EncoderParameter = new EncoderParameter(Encoder, 100L);
                g_EncoderParameters.Param[0] = EncoderParameter;
                //*************************************保存图像时编码设置结束***********************************************

                EnableExtensionMeneItem(4);
            }
            else
            {
            }
        }


        private void noiseCalculationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (m_NoiseCalculationEna)
            {
                m_NoiseCalculationEna = false;
                ToolStripMenuItem item = (ToolStripMenuItem)noiseCalculationToolStripMenuItem;
                item.Checked = false;
            }
            else
            {
                m_NoiseCalculationEna = true;
                ToolStripMenuItem item = (ToolStripMenuItem)noiseCalculationToolStripMenuItem;
                item.Checked = true;
            }
        }

        // YKB 20180510 修改网格线显示（以一个像素宽的PictureBox控件为网格线）
        private void ShowGridtoolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!m_Show_Grid)
            {
                ToolStripMenuItem item = (ToolStripMenuItem)ShowGridtoolStripMenuItem;
                item.Checked = true;

                Rectangle rc = pictBDisplay.Bounds; // 获取图像显示窗口的包络


                StringBuilder sb = new StringBuilder(255);
                GetPrivateProfileString("Grid", "GridH", "5", sb, 255, g_ConfigPath); // 水平线条数
                int LineH = int.Parse(sb.ToString()); // 读取配置文件中的整数值
                GetPrivateProfileString("Grid", "GridV", "1", sb, 255, g_ConfigPath); // 垂直线条数
                int LineV = int.Parse(sb.ToString());
                LineH = LineH < 0 ? 0 : LineH;
                LineV = LineV < 0 ? 0 : LineV;

                for (int i = 0; i < LineH; i++) // 水平网格线
                {

                    PictureBox pictureBoxGrid = new PictureBox();
                    pictureBoxGrid.BackColor = Color.Red;
                    pictureBoxGrid.Name = "pictureBoxGridH" + i.ToString();
                    //pictureBoxGrid.Location = new Point(0, (i*rc.Height) / (LineH+1));
                    //pictureBoxGrid.Size = new Size(rc.Width, 1);
                    pictureBoxGrid.Width = rc.Width;
                    pictureBoxGrid.Height = 1;
                    pictureBoxGrid.Top = ((i + 1) * rc.Height) / (LineH + 1);
                    pictureBoxGrid.Left = 0;
                    pictureBoxGrid.TabIndex = 9;
                    pictureBoxGrid.TabStop = false;
                    pictureBoxGrid.Visible = true;
                    pictBDisplay.Controls.Add(pictureBoxGrid);
                }

                for (int i = 0; i < LineV; i++) // 垂直网格线
                {

                    PictureBox pictureBoxGrid = new PictureBox();
                    pictureBoxGrid.BackColor = Color.Red;
                    pictureBoxGrid.Name = "pictureBoxGridV" + i.ToString();
                    //pictureBoxGrid.Location = new Point((i * rc.Width) / (LineV + 1), 0);
                    //pictureBoxGrid.Size = new Size(1, rc.height);
                    pictureBoxGrid.Width = 1;
                    pictureBoxGrid.Height = rc.Height;
                    pictureBoxGrid.Top = 0;
                    pictureBoxGrid.Left = ((i + 1) * rc.Width) / (LineV + 1);
                    pictureBoxGrid.TabIndex = 9;
                    pictureBoxGrid.TabStop = false;
                    pictureBoxGrid.Visible = true;
                    pictBDisplay.Controls.Add(pictureBoxGrid);
                }

                m_Show_Grid = true;
            }
            else
            {
                ToolStripMenuItem item = (ToolStripMenuItem)ShowGridtoolStripMenuItem;
                item.Checked = false;

                // 删除网格线
                for (int i = pictBDisplay.Controls.Count - 1; i >= 0; i--)
                {
                    Control ctl = pictBDisplay.Controls[i];
                    if (ctl.Name.Contains("pictureBoxGrid"))
                    {
                        pictBDisplay.Controls.RemoveAt(i);
                    }
                    //pictBDisplay.Controls.RemoveAt(i);
                }
                m_Show_Grid = false;
            }
        }

        #region Flash update
        //  For AP0100 flash update
        //  data format : type, len, data[0], data[1] ...
        //  type: 1 byte, 0: soft reset, 1: read ( read length = data[0]). 2 : commands. 3 : update flash.
        //  len: length of data[x] ...

        private int RETRY_NUM = 10;

        private void programFlashToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog openFile1 = new OpenFileDialog();
                openFile1.Filter = "bin files (*.bin)|*.bin|All files (*.*)|*.*";
                if (openFile1.ShowDialog() == DialogResult.OK)
                {
                    FileStream fs = new FileStream(openFile1.FileName, FileMode.Open);
                    if (fs.Length > 0)
                    {
                        Byte[] fileBuf = new Byte[fs.Length];
                        fs.Read(fileBuf, 0, fileBuf.Length);

                        programFlash(fileBuf);
                    }
                    fs.Close();
                }
                soft_reset();
                Thread.Sleep(500);
                readFuseID();

                System.Media.SystemSounds.Beep.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

        }

        private enum AP0100_CMD {
	        CMD_GET_LOCK 		= 0x8500,
	        CMD_LOCK_STATUS 	= 0x8501,
	        CMD_RELEASE_LOCK	= 0x8502,
	        CMD_WRITE		    = 0x8505,
	        CMD_ERASE_BLOCK		= 0x8506,
	        CMD_QUERY_DEV		= 0x8508,
	        CMD_FLASH_STATUS	= 0x8509,
	        CMD_CONFIG_DEV		= 0x850a,
            CMD_CCIMGR_GET_LOCK = 0x8D00,
            CMD_CCIMGR_LOCK_STATUS  = 0x8D01,
            CMD_CCIMGR_RELEASE_LOCK = 0x8D02,
            CMD_CCIMGR_READ     = 0x8D05,
            CMD_CCIMGR_STATUS   = 0x8D08,
        };

        private Byte [] buffer_s = new Byte [33];
        private int cmd_start_pos = 2;
        private int send_command(AP0100_CMD cmd, int time_out)
        {
            Byte[] r_buf = new Byte[8];
            int cmd_size = cmd_start_pos + 4;
            int retry;

            buffer_s[0] = 2; // type : 2, command
            buffer_s[1] = 4; // len : 4 bytes
            buffer_s[cmd_start_pos] = 0x00;
            buffer_s[cmd_start_pos + 1] = 0x40;
            buffer_s[cmd_start_pos + 2] = (byte)(((int)cmd >> 8) & 0x00ff);
            buffer_s[cmd_start_pos + 3] = (byte)((int)cmd & 0x00ff);

            retry = 0;
            while (retry < time_out)
            {
                capture.WriteCamDefectPixelTable(buffer_s);

                capture.ReadCamDefectPixelTable(out r_buf);

                if ((r_buf[0] == 0x00) && (r_buf[1] == 0x00)) // doorbell cleared
                {
                    break;
                }
                retry++;
                System.Threading.Thread.Sleep(10);
            }

            if (retry == time_out)
            {
                string erroMsg = "Error: " + cmd.ToString() + " didn't go through";
                throw new Exception(erroMsg);
            }

            return 0;
        }

        // software reset
        private void soft_reset()
        {
            byte[] buffer_s = new byte[4];

            buffer_s[0] = 0;
            buffer_s[1] = 0;
            buffer_s[2] = 0;
            buffer_s[3] = 0;
            capture.WriteCamDefectPixelTable(buffer_s);
        }

        // read data from register {addrH, addrL}
        private void read_data(byte addrH, byte addrL, int count, out byte[] r_buf)
        {
            byte[] buffer_s = new byte[4];

            buffer_s[0] = 1;
            buffer_s[1] = (byte)count;
            buffer_s[2] = addrH;
            buffer_s[3] = addrL;
            capture.WriteCamDefectPixelTable(buffer_s);

            capture.ReadCamDefectPixelTable(out r_buf);
        }

        private void write_raw_data(byte[] bufferIn)
        {
            capture.WriteCamDefectPixelTable(bufferIn);
        }

        private void write_data(byte[] buf, int buf_size, int pos)
        {

            if (buf_size > 16)
            {
                return;
            }

            buffer_s[0] = 3;
            buffer_s[1] = (byte)(buf_size + 7 + 2);
            buffer_s[cmd_start_pos + 0] = 0xfc;
            buffer_s[cmd_start_pos + 1] = 0x00;
            buffer_s[cmd_start_pos + 2] = (byte)((pos >> 24) & 0x00ff);
            buffer_s[cmd_start_pos + 3] = (byte)((pos >> 16) & 0x00ff);
            buffer_s[cmd_start_pos + 4] = (byte)((pos >> 8) & 0x00ff);
            buffer_s[cmd_start_pos + 5] = (byte)((pos) & 0x00ff);
            buffer_s[cmd_start_pos + 6] = (byte)(buf_size);

            for (int i = 0; i < buf_size; i++)
            {
                buffer_s[cmd_start_pos + 7 + i] = buf[pos+i];
            }

            write_raw_data(buffer_s);
        }

        private void programFlash(Byte[] buffer_bin)
        {
            int pos = 0;
            int page_remaining = 0;
            int steps = 0;
            byte[] r_buf;

            FlashUpdateInProgress = true;
            flashUpdatePercentage = 0;
            try
            {
                soft_reset();
                Thread.Sleep(800);
                read_data(0x00, 0x00, 2, out r_buf); // ID
                Thread.Sleep(50);
                send_command(AP0100_CMD.CMD_GET_LOCK, 5);
                Thread.Sleep(50);
                send_command(AP0100_CMD.CMD_LOCK_STATUS, 5);
                Thread.Sleep(50);
                buffer_s[0] = 2; // write data
                buffer_s[1] = (byte)(cmd_start_pos + 10);
                buffer_s[cmd_start_pos] = 0xfc; buffer_s[cmd_start_pos + 1] = 0x00;
                buffer_s[cmd_start_pos + 2] = 0x04; buffer_s[cmd_start_pos + 3] = 0x00;
                buffer_s[cmd_start_pos + 4] = 0x03; buffer_s[cmd_start_pos + 5] = 0x18;
                buffer_s[cmd_start_pos + 6] = 0x00; buffer_s[cmd_start_pos + 7] = 0x01;
                buffer_s[cmd_start_pos + 8] = 0x00; buffer_s[cmd_start_pos + 9] = 0x00;
                write_raw_data(buffer_s);
                Thread.Sleep(50);

                send_command(AP0100_CMD.CMD_CONFIG_DEV, 5);
                Thread.Sleep(50); 
                send_command(AP0100_CMD.CMD_RELEASE_LOCK, 5);
                Thread.Sleep(50);

                buffer_s[0] = 2; // write data
                buffer_s[1] = (byte)(cmd_start_pos + 18);
                buffer_s[cmd_start_pos] = 0xfc; buffer_s[cmd_start_pos + 1] = 0x00;
                buffer_s[cmd_start_pos + 2] = 0x00; buffer_s[cmd_start_pos + 3] = 0x00;
                buffer_s[cmd_start_pos + 4] = 0x00; buffer_s[cmd_start_pos + 5] = 0x00;
                buffer_s[cmd_start_pos + 6] = 0x00; buffer_s[cmd_start_pos + 7] = 0x00;
                buffer_s[cmd_start_pos + 8] = 0x00; buffer_s[cmd_start_pos + 9] = 0x00;
                buffer_s[cmd_start_pos + 10] = 0x00; buffer_s[cmd_start_pos + 11] = 0x00;
                buffer_s[cmd_start_pos + 12] = 0x00; buffer_s[cmd_start_pos + 13] = 0x00;
                buffer_s[cmd_start_pos + 14] = 0x00; buffer_s[cmd_start_pos + 15] = 0x00;
                buffer_s[cmd_start_pos + 16] = 0x00; buffer_s[cmd_start_pos + 17] = 0x00;
                write_raw_data(buffer_s);
                Thread.Sleep(50);

                send_command(AP0100_CMD.CMD_GET_LOCK, 5);
                Thread.Sleep(50); 
                send_command(AP0100_CMD.CMD_LOCK_STATUS, 5);
                Thread.Sleep(50); 
                send_command(AP0100_CMD.CMD_QUERY_DEV, 5);
                Thread.Sleep(50); 
                send_command(AP0100_CMD.CMD_FLASH_STATUS, 5);
                Thread.Sleep(50);
                read_data(0xfc, 0x00, 16, out r_buf);
                Thread.Sleep(50);

                buffer_s[0] = 2; // write data
                buffer_s[1] = (byte)(cmd_start_pos + 6);
                buffer_s[cmd_start_pos] = 0xfc; buffer_s[cmd_start_pos + 1] = 0x00;
                buffer_s[cmd_start_pos + 2] = 0x00; buffer_s[cmd_start_pos + 3] = 0x00;
                buffer_s[cmd_start_pos + 4] = 0x00; buffer_s[cmd_start_pos + 5] = 0x00;
                write_raw_data(buffer_s);

                // erase flash
                send_command(AP0100_CMD.CMD_ERASE_BLOCK, 5);
                Thread.Sleep(1000);
                send_command(AP0100_CMD.CMD_FLASH_STATUS, 1000);
                Thread.Sleep(50);

                int buf_size = buffer_bin.Length;
                int PACKET_SIZE = 11;
                pos = 0;

                while (pos < buf_size)
                {
                    if (buf_size - pos > PACKET_SIZE)
                    {
                        page_remaining = 0x0100 - (pos & 0x00ff);

                        if (page_remaining > PACKET_SIZE)
                        {
                            write_data(buffer_bin, PACKET_SIZE, pos);
                            pos += PACKET_SIZE;
                        }
                        else
                        {
                            write_data(buffer_bin, page_remaining, pos);
                            pos += page_remaining;
                        }
                    }
                    else
                    {
                        write_data(buffer_bin, buf_size - pos, pos);
                        pos = buf_size;
                    }
                    send_command(AP0100_CMD.CMD_WRITE, 50);
                    send_command(AP0100_CMD.CMD_FLASH_STATUS, 50);

                    if (pos > buf_size * steps / 100)
                    {
                        steps++;
                    }
                    flashUpdatePercentage = pos * 100 / buf_size;
                    Application.DoEvents();
                }

                Thread.Sleep(50);
                send_command(AP0100_CMD.CMD_RELEASE_LOCK, 5);
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                FlashUpdateInProgress = false;
            }
            return;
        }

        private void readFuseID()
        {
            byte[] r_buf;

            Thread.Sleep(50);
            send_command(AP0100_CMD.CMD_CCIMGR_GET_LOCK, 5);
            Thread.Sleep(50);
            send_command(AP0100_CMD.CMD_CCIMGR_LOCK_STATUS, 5);
            Thread.Sleep(50);

            buffer_s[0] = 2; // write data
            buffer_s[1] = (byte)(cmd_start_pos + 4);
            buffer_s[cmd_start_pos] = 0xfc; buffer_s[cmd_start_pos + 1] = 0x00;
            buffer_s[cmd_start_pos + 2] = 0x31; buffer_s[cmd_start_pos + 3] = 0xF4;
            write_raw_data(buffer_s);
            Thread.Sleep(50);

            buffer_s[0] = 2; // write data
            buffer_s[1] = (byte)(cmd_start_pos + 3);
            buffer_s[cmd_start_pos] = 0xfc; buffer_s[cmd_start_pos + 1] = 0x02;
            buffer_s[cmd_start_pos + 2] = 0x08; 
            write_raw_data(buffer_s);
            Thread.Sleep(50);

            send_command(AP0100_CMD.CMD_CCIMGR_READ, 5);
            Thread.Sleep(50); 
            send_command(AP0100_CMD.CMD_CCIMGR_STATUS, 5);
            Thread.Sleep(50);

            read_data(0xfc, 0x00, 8, out r_buf);
            Thread.Sleep(50); 
            
            FuseID = "";
            for (int i = 0; i < 8; i++)
            {
                FuseID += r_buf[i].ToString("X2");
            }

            return;
        }

        #endregion 

        private void saveAllImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!m_SaveAllImage)
            {
                StringBuilder sb = new StringBuilder(255);
                //判断是否存在配置文件
                if (!File.Exists(g_ConfigPath))
                {
                    WriteProfileDefault(g_ConfigPath);
                }

                //路径设置
                GetPrivateProfileString("Save", "SavePath", ".\\image", sb, 255, g_ConfigPath);
                g_SavePath = sb.ToString();
                GetPrivateProfileString("Save", "SaveSuffix", "", sb, 255, g_ConfigPath);
                g_SaveSuffix = sb.ToString();

                iNumDiff = 0;
                iLastFrameCount = capture.FrameCount; // 记录保存图像时当前帧数
                m_SaveFrameToFile = false; // 准备图像采集
                m_SaveAllImage = true; // 连续存储使能

                //MessageBox.Show("开始保存图像..."); // YKB 20180421 modify 修改每帧图像保存时菜单状态
                saveAllImageToolStripMenuItem.Text = "SaveAllImage ... ... ";
                captureImageToolStripMenuItem.Enabled = false;
            }
            else
            {
                m_SaveAllImage = false;

                //MessageBox.Show("保存图像结束！");
                saveAllImageToolStripMenuItem.Text = "SaveAllImage";
                captureImageToolStripMenuItem.Enabled = true;
            }
        }

        private void WriteProfileDefault(string ConfigPath)
        {
            WritePrivateProfileString("Save", "SavePath", ".\\image", g_ConfigPath); // YKB 20180428 图像保存目录
            WritePrivateProfileString("Save", "SaveSuffix", "", g_ConfigPath);  // YKB 20180428 图像保存后缀名
            WritePrivateProfileString("Grid", "GridH", "5", g_ConfigPath); // YKB 20180510 水平线条数
            WritePrivateProfileString("Grid", "GridV", "1", g_ConfigPath); // YKB 20180510 垂直线条数
        }

        private void EnableExtensionMeneItem(int index)
        {
            BMPToolStripMenuItem.Checked = false;
            JPGToolStripMenuItem.Checked = false;
            TIFFToolStripMenuItem.Checked = false;
            GIFToolStripMenuItem.Checked = false;
            PNGToolStripMenuItem.Checked = false;
            switch(index)
            {
                case 0:
                    BMPToolStripMenuItem.Checked = true;
                    g_Image_Extension = "bmp";
                    break;
                case 1:
                    JPGToolStripMenuItem.Checked = true;
                    g_Image_Extension = "jpg";
                    break;
                case 2:
                    TIFFToolStripMenuItem.Checked = true;
                    g_Image_Extension = "tif";
                    break;
                case 3:
                    GIFToolStripMenuItem.Checked = true;
                    g_Image_Extension = "gif";
                    break;
                case 4:
                    PNGToolStripMenuItem.Checked = true;
                    g_Image_Extension = "png";
                    break;
                default:
                    g_Image_Extension = "jpg";
                    JPGToolStripMenuItem.Checked = true;
                    break;
            }
        }

        //*************************************配置文件操作***********************************************
        // YKB 20180428 配置文件操作
        [DllImport("kernel32")] // 写入配置文件的接口
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
        [DllImport("kernel32")] // 读取配置文件的接口
        private static extern int GetPrivateProfileString(string section, string key, string def,StringBuilder retVal, int size, string filePath);
        //*************************************配置文件操作结束***********************************************

        //*************************************获取图像编码***********************************************
        ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            int j;
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                    return encoders[j];
            }
            return null;
        }
        //*************************************获取图像编码结束***********************************************
    }
}
