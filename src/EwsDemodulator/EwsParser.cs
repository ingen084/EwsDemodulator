using System;
using System.Collections.Generic;
using System.Linq;

namespace EwsDemodulator
{
	public class EwsParser
	{
		public enum EwsParserState
		{
			WaitPrefix,
			ParseBeginBlock,
			ParseEndAndTestBlock,
		}

		public event Action<EwsMessage> MassegeReceived;

		private EwsMessage CurrentMessage { get; set; }

		public EwsParserState State { get; private set; } = EwsParserState.WaitPrefix;
		public int BlockParseStage { get; private set; } = 0;
		public int BlockCount { get; private set; } = 0;
		private List<byte> BitBuffer { get; } = new List<byte>();
		/// <summary>
		/// ビット列を追加する
		/// </summary>
		/// <param name="bit">false: 0 / true: 1 / null: 不明</param>
		/// <returns>解析処理を中断するか</returns>
		public bool Append(bool? nbit)
		{
			// 終了･試験信号には92bitの無信号区間が含まれている
			if (State == EwsParserState.ParseEndAndTestBlock && BlockParseStage == 4)
			{
				BitBuffer.Add(2);
				if (BitBuffer.Count >= 90 && nbit != null)
				{
					State = EwsParserState.WaitPrefix;
					BlockParseStage = 0;
					BitBuffer.Clear();
					Console.WriteLine();
				}
				else
					return false;
			}

			// 出力
			if (nbit == false)
				Console.Write($"0");
			else if (nbit == true)
				Console.Write($"1");
			else
				Console.Write($"x");

			//TODO: 一部解析が失敗しても正常に解析できるようにする
			if (!(nbit is bool bit))
			{
				Reset();
				return true;
			}

			BitBuffer.Add((byte)(bit ? 1 : 0));

			switch (State)
			{
				case EwsParserState.WaitPrefix:
					if (BitBuffer.Count < 4)
						return false;
					if (BitBuffer[0] == 1)
					{
						for (int i = 0; i < BeginPrefixToken.Length; i++)
						{
							if (BeginPrefixToken[i] == BitBuffer[i])
								continue;
							Console.WriteLine("\n前置符号検出失敗");
							Reset();
							return true;
						}
						Console.WriteLine("\n前置符号検出 第1種/第2種");
						State = EwsParserState.ParseBeginBlock;
						BlockCount = 0;
						CurrentMessage = new EwsMessage { MessageType = EwsMessageType.Type1 };
					}
					else
					{
						for (int i = 0; i < EndAndTestPrefixToken.Length; i++)
						{
							if (EndAndTestPrefixToken[i] == BitBuffer[i])
								continue;
							Console.WriteLine("\n前置符号検出失敗");
							Reset();
							return true;
						}
						Console.WriteLine("\n前置符号検出 試験/終了");
						State = EwsParserState.ParseEndAndTestBlock;
						if (CurrentMessage == null)
							CurrentMessage = new EwsMessage { MessageType = EwsMessageType.EndAndTest };
					}
					BlockParseStage = 0;
					BitBuffer.Clear();
					break;
				case EwsParserState.ParseBeginBlock:
				case EwsParserState.ParseEndAndTestBlock:
					if (BitBuffer.Count == 16)
					{
						Console.Write(" ");
						return false;
					}
					if (BitBuffer.Count < 32)
						return false;
					Console.WriteLine();
					BlockParseStage++;

					if (BitBuffer[0] == 0)
					{
						for (int i = 0; i < Type1StartAndEndFixedToken.Length; i++)
						{
							if (Type1StartAndEndFixedToken[i] == BitBuffer[i])
								continue;
							Console.WriteLine("\n固定符号検出失敗");
							Reset();
							return true;
						}
						Console.Write($" {(State == EwsParserState.ParseBeginBlock ? "第1種" : "終了/試験")}: ");
					}
					else if (BitBuffer[0] == 1)
					{
						for (int i = 0; i < Type2StartFixedToken.Length; i++)
						{
							if (Type2StartFixedToken[i] == BitBuffer[i])
								continue;
							Console.WriteLine("\n固定符号検出失敗");
							Reset();
							return true;
						}
						Console.Write(" 第2種: ");
						CurrentMessage.MessageType = EwsMessageType.Type2;
					}

					switch (BlockParseStage)
					{
						case 1:
							{
								var region = SearchToken(AreaTokens, BitBuffer, 18, 12) ?? "不明";
								Console.WriteLine($"地域: {region}");
								CurrentMessage.AreaName = region;
							}
							break;
						case 2:
							{
								bool isEvenBlock = BitBuffer[24] == 1;
								var day = SearchToken(DayTokens, BitBuffer, 19, 5);
								var month = SearchToken(MonthTokens, BitBuffer, 25, 5);
								Console.WriteLine($"{(isEvenBlock ? "*" : " ")}{month}月 {day}日");
								if (day == null || month == null)
									return true;


								if (CurrentMessage.Time == null)
									CurrentMessage.Time = new EwsDateTime();
								CurrentMessage.Time.IsRealtime = !isEvenBlock;
								CurrentMessage.Time.Day = day.GetValueOrDefault();
								CurrentMessage.Time.Month = month.GetValueOrDefault();
							}
							break;
						case 3:
							{
								bool isEvenBlock = BitBuffer[24] == 1;
								var hour = SearchToken(HourTokens, BitBuffer, 19, 5);
								var year = SearchToken(YearTokens, BitBuffer, 25, 5);
								Console.WriteLine($"{(isEvenBlock ? "*" : " ")}20x{year}年 {hour}時");
								if (hour == null || year == null)
									return true;

								if (CurrentMessage.Time == null)
									CurrentMessage.Time = new EwsDateTime();
								CurrentMessage.Time.IsRealtime = !isEvenBlock;
								CurrentMessage.Time.Hour = hour.GetValueOrDefault();
								CurrentMessage.Time.Year = year.GetValueOrDefault();

								BlockCount++;
								Console.WriteLine($"=== ブロック{BlockCount} 終 ===\n");

								CurrentMessage.BlockId = BlockCount;
								MassegeReceived?.Invoke(CurrentMessage);
								CurrentMessage.AreaName = null;
								CurrentMessage.Time = null;

								if (State == EwsParserState.ParseEndAndTestBlock)
								{
									//State = EwsParserState.WaitPrefix;
									BlockParseStage++;
									if (BlockCount == 4) // 4ブロック見たら終了させる
									{
										Reset();
										return true;
									}
								}
								else
								{
									BlockParseStage = 0;
									if (BlockCount == 10) // 10ブロック見たら終了させる
									{
										Reset();
										return true;
									}
								}
							}
							break;
					}
					BitBuffer.Clear();
					break;
			}
			return false;
		}

		public void Reset()
		{
			BitBuffer.Clear();
			BlockCount = 0;
			State = EwsParserState.WaitPrefix;
		}

		// 前置符号
		private static readonly byte[] BeginPrefixToken = new byte[] { 1, 1, 0, 0 };
		private static readonly byte[] EndAndTestPrefixToken = new byte[] { 0, 0, 1, 1 };

		// 固定符号
		private static readonly byte[] Type1StartAndEndFixedToken = new byte[] { 0, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 1 };
		private static readonly byte[] Type2StartFixedToken = new byte[] { 1, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 1, 0 };

		private static int? SearchToken(Dictionary<int, int> dictionary, List<byte> buffer, int offset, int length)
		{
			var code = 0;
			for (int i = 0; i < length; i++)
				code += buffer[offset + i] << length - 1 - i;
			if (dictionary.ContainsKey(code))
				return dictionary[code];
			return null;
		}
		private static string SearchToken(Dictionary<int, string> dictionary, List<byte> buffer, int offset, int length)
		{
			var code = 0;
			for (int i = 0; i < length; i++)
				code += buffer[offset + i] << length - 1 - i;
			if (dictionary.ContainsKey(code))
				return dictionary[code];
			return null;
		}

		private static readonly Dictionary<int, string> AreaTokens = new Dictionary<int, string>
		{
			{ 0b001101001101, "全国" },

			{ 0b010110100101, "関東広域圏" },
			{ 0b011100101010, "中京広域圏" },
			{ 0b100011010101, "近畿広域圏" },
			{ 0b011010011001, "鳥取・島根圏" },
			{ 0b010101010011, "岡山・香川圏" },

			{ 0b000101101011,"北海道" },
			{ 0b010001100111,"青森県" },
			{ 0b010111010100,"岩手県" },
			{ 0b011101011000,"宮城県" },
			{ 0b101011000110,"秋田県" },
			{ 0b111001001100,"山形県" },
			{ 0b000110101110,"福島県" },
			{ 0b110001101001,"茨城県" },
			{ 0b111000111000,"栃木県" },
			{ 0b100110001011,"群馬県" },
			{ 0b011001001011,"埼玉県" },
			{ 0b000111000111,"千葉県" },
			{ 0b101010101100,"東京都" },
			{ 0b010101101100,"神奈川県" },
			{ 0b010011001110,"新潟県" },
			{ 0b010100111001,"富山県" },
			{ 0b011010100110,"石川県" },
			{ 0b100100101101,"福井県" },
			{ 0b110101001010,"山梨県" },
			{ 0b100111010010,"長野県" },
			{ 0b101001100101,"岐阜県" },
			{ 0b101001011010,"静岡県" },
			{ 0b100101100110,"愛知県" },
			{ 0b001011011100,"三重県" },
			{ 0b110011100100,"滋賀県" },
			{ 0b010110011010,"京都府" },
			{ 0b110010110010,"大阪府" },
			{ 0b011001110100,"兵庫県" },
			{ 0b101010010011,"奈良県" },
			{ 0b001110010110,"和歌山県" },
			{ 0b110100100011,"鳥取県" },
			{ 0b001100011011,"島根県" },
			{ 0b001010110101,"岡山県" },
			{ 0b101100110001,"広島県" },
			{ 0b101110011000,"山口県" },
			{ 0b111001100010,"徳島県" },
			{ 0b100110110100,"香川県" },
			{ 0b000110011101,"愛媛県" },
			{ 0b001011100011,"高知県" },
			{ 0b011000101101,"福岡県" },
			{ 0b100101011001,"佐賀県" },
			{ 0b101000101011,"長崎県" },
			{ 0b100010100111,"熊本県" },
			{ 0b110010001101,"大分県" },
			{ 0b110100011100,"宮崎県" },
			{ 0b110101000101,"鹿児島県" },
			{ 0b001101110010,"沖縄県" },
		};
		private static readonly Dictionary<int, int> DayTokens = new Dictionary<int, int>
		{
			{ 0b10000, 1 },
			{ 0b01000, 2 },
			{ 0b11000, 3 },
			{ 0b00100, 4 },
			{ 0b10100, 5 },
			{ 0b01100, 6 },
			{ 0b11100, 7 },
			{ 0b00010, 8 },
			{ 0b10010, 9 },
			{ 0b01010, 10 },
			{ 0b11010, 11 },
			{ 0b00110, 12 },
			{ 0b10110, 13 },
			{ 0b01110, 14 },
			{ 0b11110, 15 },
			{ 0b00001, 16 },
			{ 0b10001, 17 },
			{ 0b01001, 18 },
			{ 0b11001, 19 },
			{ 0b00101, 20 },
			{ 0b10101, 21 },
			{ 0b01101, 22 },
			{ 0b11101, 23 },
			{ 0b00011, 24 },
			{ 0b10011, 25 },
			{ 0b01011, 26 },
			{ 0b11011, 27 },
			{ 0b00111, 28 },
			{ 0b10111, 29 },
			{ 0b01111, 30 },
			{ 0b11111, 31 },
		};
		private static readonly Dictionary<int, int> MonthTokens = new Dictionary<int, int>
		{
			{ 0b10001, 1 },
			{ 0b01001, 2 },
			{ 0b11001, 3 },
			{ 0b00101, 4 },
			{ 0b10101, 5 },
			{ 0b01101, 6 },
			{ 0b11101, 7 },
			{ 0b00011, 8 },
			{ 0b10011, 9 },
			{ 0b01011, 10 },
			{ 0b11011, 11 },
			{ 0b00111, 12 },
		};
		private static readonly Dictionary<int, int> HourTokens = new Dictionary<int, int>
		{
			{ 0b00011, 0 },
			{ 0b10011, 1 },
			{ 0b01011, 2 },
			{ 0b11011, 3 },
			{ 0b00111, 4 },
			{ 0b10111, 5 },
			{ 0b01111, 6 },
			{ 0b11111, 7 },
			{ 0b00001, 8 },
			{ 0b10001, 9 },
			{ 0b01001, 10 },
			{ 0b11001, 11 },
			{ 0b00101, 12 },
			{ 0b10101, 13 },
			{ 0b01101, 14 },
			{ 0b11101, 15 },
			{ 0b00010, 16 },
			{ 0b10010, 17 },
			{ 0b01010, 18 },
			{ 0b11010, 19 },
			{ 0b00110, 20 },
			{ 0b10110, 21 },
			{ 0b01110, 22 },
			{ 0b11110, 23 },
		};
		private static readonly Dictionary<int, int> YearTokens = new Dictionary<int, int>
		{
			{ 0b10101, 5 },
			{ 0b01101, 6 },
			{ 0b11101, 7 },
			{ 0b00011, 8 },
			{ 0b10011, 9 },
			{ 0b01011, 0 },
			{ 0b10001, 1 },
			{ 0b01001, 2 },
			{ 0b11001, 3 },
			{ 0b00101, 4 },
		};
	}
}
