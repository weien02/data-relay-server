namespace DedicatedServer.Demo.JumpingGame
{
    public readonly struct AvatarJumpReleasedEvent
    {
        public readonly float jumpPower;

        public AvatarJumpReleasedEvent(float jumpPower)
        {
            this.jumpPower = jumpPower;
        }
    }
}
