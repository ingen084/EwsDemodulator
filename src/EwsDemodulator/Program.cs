using NAudio.Dsp;
using NAudio.Wave;
using System;

namespace EwsDemodulator
{
	class Program
	{
		static void Main(string[] args)
		{
			using var capture = new WasapiLoopbackCapture();
			capture.RecordingStopped += (s, e) => Console.WriteLine("Capture Stopped!");

			// LPF / HPF
			var filter = BiQuadFilter.LowPassFilter(capture.WaveFormat.SampleRate, 3000, 1);
			filter.SetHighPassFilter(capture.WaveFormat.SampleRate, 250, 1);

			var render = new EwsSampleDemodulator(capture.WaveFormat.SampleRate);
			render.MassegeReceived += m => Console.WriteLine($@"
==== EWSブロック受信 ===
Type: {m.MessageType}
Area: {m.AreaName}
Time: {m.Time.ToDateTime():yyyy/MM/dd HH時}
=======================
");

			ulong index = 0;
			//using var wWriter = new WaveFileWriter("capture.wav", WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate, 1));
			capture.DataAvailable += (s, e) =>
			{
				var array = WaveToSampleAray(e.Buffer, e.BytesRecorded);

				// フィルタの適用
				for (var i = 0; i < array.Length; i++)
					array[i] = filter.Transform(array[i]);

				render.Parse(index, array, 0, array.Length);
				index += (uint)array.Length;
			};

			capture.StartRecording();
			Console.ReadLine();
			capture.StopRecording();
		}

		public static float[] WaveToSampleAray(byte[] input, int length)
		{
			if (length == 0)
				return Array.Empty<float>();
			var array = new float[length / 8];
			for (int i = 0; i < length / 8; i++)
				array[i] = (float)(BitConverter.ToSingle(input, i * 8) * .5 + BitConverter.ToSingle(input, i * 8 + 4) * .5);
			return array;
		}
	}
}
