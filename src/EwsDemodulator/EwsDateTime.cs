using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EwsDemodulator
{
	public class EwsDateTime
	{
		/// <summary>
		/// 年時区分符号が信号送出の時に対応する符号であるか
		/// </summary>
		public bool IsRealtime { get; set; }

		/// <summary>
		/// 年下1桁
		/// </summary>
		public int Year { get; set; }
		/// <summary>
		/// 月
		/// </summary>
		public int Month { get; set; }
		/// <summary>
		/// 日
		/// </summary>
		public int Day { get; set; }
		/// <summary>
		/// 時間
		/// </summary>
		public int Hour { get; set; }

		public DateTime ToDateTime()
		{
			var now = DateTime.Now;
			// 現時刻から+-10年をみて一番近い日時を選択(5年離れている場合を考慮しない)
			var year = Enumerable.Range((now.Year / 10) - 1, 3).OrderBy(y => Math.Abs(now.Year - (y * 10 + Year))).First() * 10 + Year;

			return new DateTime(year, Month, Day, Hour, 0, 0);
		}
	}
}
