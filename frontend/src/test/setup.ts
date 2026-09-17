import '@testing-library/jest-dom/vitest';

class ResizeObserverStub {
	observe() {
		return undefined;
	}

	unobserve() {
		return undefined;
	}

	disconnect() {
		return undefined;
	}
}

Object.defineProperty(globalThis, 'ResizeObserver', {
	configurable: true,
	writable: true,
	value: ResizeObserverStub,
});