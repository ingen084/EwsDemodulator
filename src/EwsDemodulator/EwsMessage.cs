namespace EwsDemodulator
{
	public class EwsMessage
	{
		public int BlockId { get; set; }
		public EwsMessageType MessageType { get; set; }
		public string AreaName { get; set; }
		public EwsDateTime Time { get; set; }
	}
}
