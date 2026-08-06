type StoryContext = {
    runSeed: number;
    streamKey: string;
};

type StoryResult = {
    options: unknown[];
};

export function execute(_ctx: StoryContext): StoryResult {
    return { options: [] };
}
