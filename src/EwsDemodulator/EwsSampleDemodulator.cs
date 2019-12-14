using NAudio.Dsp;
using NAudio.Wave;
using System;

namespace EwsDemodulator
{
	public class EwsSampleDemodulator
	{
		public event Action<EwsMessage> MassegeReceived;

		private BiQuadFilter Filter { get; }
		private EwsParser Parser { get; }

		public EwsSampleDemodulator(WaveFormat format)
		{
			WaveFormat = format;
			Filter = BiQuadFilter.LowPassFilter(WaveFormat.SampleRate, 3000, 1);
			Filter.SetHighPassFilter(WaveFormat.SampleRate, 250, 1);
			Parser = new EwsParser();
			Parser.MassegeReceived += m => MassegeReceived?.Invoke(m);
		}

		public WaveFormat WaveFormat { get; }
		public uint EwsBitCount => (uint)(WaveFormat.SampleRate / 64);
		public uint FrequencyChangeCount => (uint)(WaveFormat.SampleRate / (64 * 4));

		// 許容する誤差
		public const uint BitSampleTolerance = 10;
		public uint LowBitSampleCount => (uint)(WaveFormat.SampleRate / 640);
		public uint HighBitSampleCount => (uint)(WaveFormat.SampleRate / 1024);

		int minVolumeSampleCount = 0;

		private enum SearchWaveState
		{
			/// <summary>
			/// 消音区間待機中
			/// </summary>
			WaitSilence,
			/// <summary>
			/// しきい値に入るのを待っている状態
			/// </summary>
			SearchWaveIn,
		}

		/// <summary>
		/// 現在の状態
		/// </summary>
		private SearchWaveState State { get; set; } = SearchWaveState.WaitSilence;
		/// <summary>
		/// 最後にゼロクロスした位置
		/// </summary>
		private ulong LastCrossingPosition { get; set; }
		/// <summary>
		/// 周波数が変更された位置
		/// </summary>
		private ulong FrequencyChangedPosition { get; set; }
		/// <summary>
		/// 次のビットが来るであろう位置
		/// </summary>
		private ulong NextStateCheckPosition { get; set; }
		/// <summary>
		/// 一つ前のサンプルの値
		/// </summary>
		private float LastSampleValue { get; set; }
		/// <summary>
		/// 最終検出値
		/// </summary>
		private bool? LastWaveState { get; set; }
		/// <summary>
		/// 安定化した検出値
		/// </summary>
		private bool? StableWaveState { get; set; }
		public void Parse(ulong startIndex, float[] buffer, int offset, int readed)
		{
			for (var i = 0; i < readed; i++)
				buffer[offset + i] = Filter.Transform(buffer[offset + i]);

			var silenceTimeSampleCount = WaveFormat.SampleRate;
			for (int i = 0; i < readed; i++)
			{
				var currentWave = buffer[offset + i];
				var currentPosotion = startIndex + (uint)i;

				switch (State)
				{
					case SearchWaveState.WaitSilence:
						if (Math.Abs(currentWave) < 0.05)
						{
							if (minVolumeSampleCount < silenceTimeSampleCount)
							{
								minVolumeSampleCount++;
								if (minVolumeSampleCount >= silenceTimeSampleCount)
									Console.WriteLine($"無音");
							}
						}
						else if (minVolumeSampleCount >= silenceTimeSampleCount)
						{
							// 無音後の音声開始
							minVolumeSampleCount = 0;

							Console.WriteLine($"開始");

							LastWaveState = null;
							LastCrossingPosition = currentPosotion;
							NextStateCheckPosition = 0;
							FrequencyChangedPosition = long.MaxValue;
							LastSampleValue = currentWave;
							State = SearchWaveState.SearchWaveIn;
						}
						else // 無音でなくて規定時間たっていなかったらカウンタリセットし続ける
							minVolumeSampleCount = 0;
						continue;

					case SearchWaveState.SearchWaveIn:
						// ゼロクロスポイント発見
						if (LastSampleValue <= 0 && currentWave > 0)
						{
							var length = currentPosotion - LastCrossingPosition;
							// 周波数計算
							//var freq = WaveFormat.SampleRate / (double)length;

							// 3値化
							bool? waveState = null;
							if (Math.Abs((int)(length - LowBitSampleCount)) <= BitSampleTolerance)
								waveState = false;
							else if (Math.Abs((int)(length - HighBitSampleCount)) <= BitSampleTolerance)
								waveState = true;

							if (waveState != LastWaveState) //前回の波形から変更を検出
							{
								LastWaveState = waveState;
								FrequencyChangedPosition = currentPosotion;
								// 次の判定時間をセット
								NextStateCheckPosition = currentPosotion + EwsBitCount / 2;
							}
							else if ((currentPosotion - FrequencyChangedPosition) >= FrequencyChangeCount) //変更されてから規定時間が経てば安定した値を更新
							{
								StableWaveState = waveState;
								FrequencyChangedPosition = ulong.MaxValue;
							}

							// 安定しているかに関わらず一定時間が経過していたら最後の安定していた値でパース
							if (NextStateCheckPosition <= currentPosotion)
							{
								if (Parser.Append(StableWaveState))
								{
									State = SearchWaveState.WaitSilence;
									minVolumeSampleCount = 0;
									Console.WriteLine($"\n終了");
									break;
								}
								NextStateCheckPosition = currentPosotion + EwsBitCount;
							}
							LastCrossingPosition = currentPosotion;
							break;
						}
						break;
				}

				LastSampleValue = currentWave;
			}
		}
	}
}
