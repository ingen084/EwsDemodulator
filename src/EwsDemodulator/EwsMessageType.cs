namespace EwsDemodulator
{
	public enum EwsMessageType
	{
		/// <summary>
		/// 試験/終了信号
		/// </summary>
		EndAndTest,
		/// <summary>
		/// 第1種 (避難指示)
		/// </summary>
		Type1,
		/// <summary>
		/// 第2種 (津波)
		/// </summary>
		Type2,
	}
	public static class EwsMessageTypeExtensions
	{
		public static string ToJapaneseString(this EwsMessageType type)
			=> type switch
			{
				EwsMessageType.EndAndTest => "試験･終了",
				EwsMessageType.Type1 => "第1種",
				EwsMessageType.Type2 => "第2種",
				_ => "不明"
			};
	}
}
