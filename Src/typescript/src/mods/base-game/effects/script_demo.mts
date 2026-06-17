export function execute(ctx: { nextInt(min: number, max: number): number }) {
    const amount = ctx.nextInt(4, 8);
    return {
        proposedEffects: [{ kind: 'Damage', params: { amount } }],
    };
}
