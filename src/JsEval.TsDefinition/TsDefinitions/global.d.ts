/**
 * A .NET array exposed to script as a live view: reads and element writes go
 * straight through to the underlying array, and so do in-place reorderings
 * such as sort() and reverse(). The array is fixed-size, so anything that
 * would grow or shrink it — push, pop, shift, unshift, splice, or assigning
 * length — throws at runtime and is therefore rejected here.
 *
 * Expose a .NET List<T> instead when a script needs to add or remove items.
 */
interface ClrArray<T> extends ReadonlyArray<T> {
    [index: number]: T;
    sort(compareFn?: (a: T, b: T) => number): this;
    reverse(): this;
    fill(value: T, start?: number, end?: number): this;
}

declare function NewObject<T>(typeName: string, ...args: any[]): T;

declare function require(moduleName: string): any;

declare function fetch(url: string, options?: {
    method?: string;
    headers?: Record<string, string>;
    body?: string;
}): Promise<{
    ok: boolean;
    status: number;
    statusText: string;
    url: string;
    redirected: boolean;
    headers: Record<string, string>;
    text(): Promise<string>;
    json(): Promise<any>;
    arrayBuffer(): Promise<ArrayBuffer>;
}>;

declare var fetchOptions: {
    ignoreCertificateErrors(value: boolean): typeof fetchOptions;
    timeout(seconds: number): typeof fetchOptions;
    proxy(url: string): typeof fetchOptions;
} | undefined;
