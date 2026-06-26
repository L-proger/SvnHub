(() => {
    const startChar = '_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';
    const char = '-0123456789' + startChar;
    const replaceRe = new RegExp('^[^' + startChar + ']|[^\\' + char + ']', 'g');

    function random(length) {
        length = length || 12;
        return getRandomString(1, startChar) + getRandomString(length - 1, char);
    }

    function sanitize(source) {
        return String(source || '').replace(replaceRe, '_');
    }

    function ensure(maybeId, length) {
        return typeof maybeId === 'string' ? sanitize(maybeId) : random(length);
    }

    function getRandomString(length, alphabet) {
        let result = '';
        while (length > 0) {
            length -= 1;
            result += alphabet[Math.floor(Math.random() * alphabet.length)];
        }
        return result;
    }

    window.TracespaceXmlId = { random, sanitize, ensure };
})();
