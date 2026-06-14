using System.IO;
using System.Text;

namespace Game.Core
{
    // แปลง GameAction ↔ byte[] — สำหรับ online: client ส่ง intent ไปให้ host (host ตรวจ+apply)
    //   type byte: 0=TakeCoins 1=BuyCard 2=ReserveCard 3=AnswerQuiz
    public static class GameActionCodec
    {
        public static byte[] Serialize(GameAction a)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);

            switch (a)
            {
                case TakeCoinsAction t:
                    w.Write((byte)0);
                    w.Write(t.seat);
                    int n = t.coins?.Length ?? 0;
                    w.Write(n);
                    for (int i = 0; i < n; i++) w.Write(t.coins[i]);
                    break;

                case BuyCardAction b:
                    w.Write((byte)1);
                    w.Write(b.seat);
                    w.Write(b.cardId ?? "");
                    w.Write(b.fromReserve);
                    break;

                case ReserveCardAction rv:
                    w.Write((byte)2);
                    w.Write(rv.seat);
                    w.Write(rv.cardId ?? "");
                    break;

                case AnswerQuizAction q:
                    w.Write((byte)3);
                    w.Write(q.seat);
                    w.Write(q.questionId ?? "");
                    w.Write(q.choiceIndex);
                    break;

                default:
                    throw new IOException("GameActionCodec: ไม่รู้จัก action type");
            }

            w.Flush();
            return ms.ToArray();
        }

        public static GameAction Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms, Encoding.UTF8);

            byte type = r.ReadByte();
            switch (type)
            {
                case 0:
                    var t = new TakeCoinsAction { seat = r.ReadInt32() };
                    int n = r.ReadInt32();
                    t.coins = new int[n];
                    for (int i = 0; i < n; i++) t.coins[i] = r.ReadInt32();
                    return t;

                case 1:
                    return new BuyCardAction { seat = r.ReadInt32(), cardId = r.ReadString(), fromReserve = r.ReadBoolean() };

                case 2:
                    return new ReserveCardAction { seat = r.ReadInt32(), cardId = r.ReadString() };

                case 3:
                    return new AnswerQuizAction { seat = r.ReadInt32(), questionId = r.ReadString(), choiceIndex = r.ReadInt32() };

                default:
                    return null;
            }
        }
    }
}
