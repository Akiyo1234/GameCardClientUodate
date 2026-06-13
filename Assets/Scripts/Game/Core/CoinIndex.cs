namespace Game.Core
{
    // ค่าคงที่ดัชนีเหรียญ/โบนัส — อ้างอิงจากโค้ดเดิม (PlayerUI.coins[6], bonuses[5], bankCoins[6])
    //   coins:   0=CPU 1=RAM 2=Network 3=Storage 4=Security 5=Gold(wildcard)
    //   bonuses: 0=CPU 1=RAM 2=Network 3=Storage 4=Security  (ไม่มี Gold)
    public static class CoinIndex
    {
        public const int Cpu = 0;
        public const int Ram = 1;
        public const int Network = 2;
        public const int Storage = 3;
        public const int Security = 4;
        public const int Gold = 5;

        public const int ColorCount = 5; // จำนวนสีปกติ (ไม่รวมทอง)
        public const int TotalCount = 6; // รวมทอง
    }
}
