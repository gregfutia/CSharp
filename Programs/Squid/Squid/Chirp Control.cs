using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NationalInstruments.DAQmx;
using NationalInstruments;

namespace Squid
{
    public partial class ChirpControl : Form
    {
        private Task taskObj;
        private double sampleFreq = 0;

        public ChirpControl()
        {
            InitializeComponent();

            physicalChannelComboBox.Items.AddRange(DaqSystem.Local.GetPhysicalChannels(PhysicalChannelTypes.AO, PhysicalChannelAccess.External));
            if (physicalChannelComboBox.Items.Count > 0)
                physicalChannelComboBox.SelectedIndex = 1;
        }

        public double MaxValue
        {
            get
            {
                if(maxValueNumeric.Value < minValueNumeric.Value)
                    return (Convert.ToDouble(minValueNumeric.Value));
                else
                    return (Convert.ToDouble(maxValueNumeric.Value));
            }
        }

        public double MinValue
        {
            get
            {
                if(minValueNumeric.Value > maxValueNumeric.Value)
                    return(Convert.ToDouble(maxValueNumeric.Value));

                else 
                    return (Convert.ToDouble(minValueNumeric.Value));
            }
        }

        public double TimeDuration
        {
            get
            {
                return (Convert.ToDouble(durationNumeric.Value));
            }
        }

        public ZDataPoint StartChirp(AcquisitionController acqConObj)
        {
            double maxFrequency = Convert.ToDouble(stopFreqNumeric.Value);
            double minFrequency = Convert.ToDouble(startFreqNumeric.Value);
            ZDataPoint zDataPoint = null;
            
            if(maxFrequency < minFrequency)
            {
                minFrequency = maxFrequency;
                maxFrequency = Convert.ToDouble(startFreqNumeric.Value);
            }
            
            sampleFreq = maxFrequency*10;


            try
            {
                if (taskObj != null)
                {
                    taskObj.WaitUntilDone();
                    taskObj.Dispose();
                }

                taskObj = new Task("Chrip Task");

                // setup the channel
                taskObj.AOChannels.CreateVoltageChannel(physicalChannelComboBox.Text,
                    "",
                    Convert.ToDouble(minValueNumeric.Value),
                    Convert.ToDouble(maxValueNumeric.Value),
                    AOVoltageUnits.Volts);

                taskObj.Control(TaskAction.Verify);

                // create the waveform, it's length will be used in the sample timing
                double []waveForm = GenerateWaveformData(Convert.ToDouble(startFreqNumeric.Value),
                    Convert.ToDouble(stopFreqNumeric.Value),
                    Convert.ToDouble(durationNumeric.Value),
                    sampleFreq);

                
                // configure the clock
                taskObj.Timing.ConfigureSampleClock("",
                    sampleFreq,
                    SampleClockActiveEdge.Rising,
                    SampleQuantityMode.FiniteSamples, waveForm.Length);

                taskObj.Timing.SampleTimingType = SampleTimingType.SampleClock;
                
                AnalogSingleChannelWriter writer =
                    new AnalogSingleChannelWriter(taskObj.Stream);

                writer.WriteMultiSample(false, waveForm);

                bool bRunning = acqConObj.IsRunning;

                acqConObj.StartContinousUpdate(this);

                taskObj.Start();

                // make list of ZData Points to collect
                List <ZDataPoint> dataList = new List<ZDataPoint>();
                dataList.Add(acqConObj.CurrentTrace);
                double T = dataList[0].DataSeriesArr[0].timeDuration;
                double capTime = T;

                // capture all the data
                while(capTime < TimeDuration + T)
                {
                    while(acqConObj.CurrentTrace == dataList[dataList.Count-1])
                    {
                        // sleep for 1/4 the period
                        Thread.Sleep((int) Math.Round(T*1000/4));
                    }                    
    
                    dataList.Add(acqConObj.CurrentTrace);
                    capTime += T;
                }

                // create the data series
                List <DataSeries> dsList = new List<DataSeries>();

                for(int i = 0; i < dataList[0].DataSeriesArr.Length; i++)
                {
                    DataSeries dataSerisObj = new DataSeries(dataList[0].DataSeriesArr[i].NumPoints*dataList.Count,
                        dataList[0].DataSeriesArr[i].SampleRate,
                        DataSeries.AmpUnits.dBmV,
                        50);

                    dsList.Add(dataSerisObj);
                }

                // copy the data into the dsLists
                int index = 0;
                for(int i = 0; i < dataList.Count; i++)
                {
                    for(int j = 0; j < dataList[i].DataSeriesArr[0].NumPoints; j++)
                    {
                        for(int k = 0; k < dsList.Count; k++)
                        {
                            dsList[k].Y_t[index] = dataList[i].DataSeriesArr[k].Y_t[j];
                        }
                        index++;
                    }
                }

                // convert it to a continous array
                zDataPoint = new ZDataPoint(dsList.ToArray());

                if (bRunning == false)
                    acqConObj.StopContinousUpdate();
            }
            catch (DaqException ex)
            {
                MessageBox.Show(ex.Message);
                taskObj.Dispose();
                taskObj = null;
            }

            return (zDataPoint);
        }

        private double[] GenerateWaveformData(double fStart, double fStop, double tDuration, double fSample)
        {
            int numSamples = (int) Math.Ceiling(fSample*tDuration);
            double[] y_t = new double[numSamples];

            double minValue = MinValue;
            double maxValue = MaxValue;
            

            double amplitude = (maxValue - minValue)/2;
            double offset =  (minValue + maxValue)/2;
            // chirp parameters f = a*t+b
            double b = fStop;
            double a = (fStop - fStart) / tDuration;
            double t = 0;
            double f = fStart;

            for (int i = 0; i < numSamples; i++)
            {
                t = i/fSample; // the time point
                f = a * t + b; // the frequency
                
                // the cosine wave
                y_t[i] = amplitude*Math.Cos(2*Math.PI*f*t) + offset;
            }
            
            return (y_t);
        }
    }
}