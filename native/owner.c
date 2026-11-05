/* Copyright (c) 2026 Lofrano Tecnologia. SPDX-License-Identifier: MIT */
#include <sys/types.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <sys/prctl.h>
#include <poll.h>
#include <signal.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#define OUTPUT_CAP (1024U * 1024U)
static long long milliseconds(void) {
    struct timespec t;
    if (clock_gettime(CLOCK_MONOTONIC, &t) != 0) _exit(90);
    return (long long)t.tv_sec * 1000 + t.tv_nsec / 1000000;
}
static int exited(pid_t child) {
    siginfo_t info;
    memset(&info, 0, sizeof info);
    if (waitid(P_PID, (id_t)child, &info, WEXITED | WNOHANG | WNOWAIT) != 0) {
        if (errno == EINTR) return 0;
        _exit(91);
    }
    return info.si_pid == child;
}
static void nonblocking(int fd) {
    int flags = fcntl(fd, F_GETFL);
    if (flags < 0 || fcntl(fd, F_SETFL, flags | O_NONBLOCK) < 0) _exit(92);
}
static int consume(int fd, char *output, size_t *used, int keep, size_t *seen, size_t cap) {
    char buffer[4096];
    for (int i = 0; i < 16; i++) {
        ssize_t n = read(fd, buffer, sizeof buffer);
        if (n > 0) {
            *seen += (size_t)n;
            if (*seen > cap) return -1;
            if (keep) { memcpy(output + *used, buffer, (size_t)n); *used += (size_t)n; }
        } else if (n == 0 || errno == EAGAIN || errno == EWOULDBLOCK) return 0;
        else if (errno != EINTR) return -1;
    }
    return 0;
}
static int emit(const char *output, size_t length) {
    nonblocking(STDOUT_FILENO);
    size_t sent = 0;
    long long end = milliseconds() + 1000;
    while (sent < length && milliseconds() < end) {
        ssize_t n = write(STDOUT_FILENO, output + sent, length - sent);
        if (n > 0) sent += (size_t)n;
        else if (n < 0 && errno != EAGAIN && errno != EINTR) return -1;
        else { struct pollfd p = { STDOUT_FILENO, POLLOUT, 0 }; (void)poll(&p, 1, 10); }
    }
    return sent == length ? 0 : -1;
}
static int uri_encode(const char *source, char *target, size_t capacity) {
    static const char hex[] = "0123456789ABCDEF";
    size_t used = 0;
    for (const unsigned char *p = (const unsigned char *)source; *p; p++) {
        int safe = (*p >= 'a' && *p <= 'z') || (*p >= 'A' && *p <= 'Z') ||
                   (*p >= '0' && *p <= '9') || *p == '-' || *p == '_' || *p == '.';
        size_t need = safe ? 1 : 3;
        if (used + need + 1 > capacity) return -1;
        if (safe) target[used++] = (char)*p;
        else { target[used++] = '%'; target[used++] = hex[*p >> 4]; target[used++] = hex[*p & 15]; }
    }
    target[used] = 0;
    return 0;
}
int main(int argc, char **argv) {
    if (argc != 4 || getuid() == 0) return 2;
    int server = strcmp(argv[1], "server") == 0;
    int seed = strcmp(argv[1], "seed") == 0;
    int modify = strcmp(argv[1], "modify") == 0;
    int oracle = strcmp(argv[1], "oracle") == 0;
    if (!server && !seed && !modify && !oracle) return 2;
    if (argv[2][0] != '/' || strlen(argv[2]) > 512 || strpbrk(argv[2], "\r\n") != NULL) return 2;
    struct stat state;
    if (lstat(argv[2], &state) != 0 || !S_ISDIR(state.st_mode) || state.st_uid != getuid() || (state.st_mode & 0777) != 0700) return 2;
    char *end;
    long port = strtol(argv[3], &end, 10);
    if (*argv[3] == 0 || *end != 0 || port < 1 || port > 65535) return 2;
    char config[1024], input[1024], socket_path[1024], encoded[3072], local_uri[3100], tcp_uri[80], listeners[3200], ldapconf[1024];
    (void)snprintf(config, sizeof config, "%s/slapd.conf", argv[2]);
    (void)snprintf(input, sizeof input, "%s/%s", argv[2], seed ? "seed.ldif" : "batch.ldif");
    (void)snprintf(socket_path, sizeof socket_path, "%s/socket", argv[2]);
    (void)snprintf(ldapconf, sizeof ldapconf, "%s/ldap.conf", argv[2]);
    if (uri_encode(socket_path, encoded, sizeof encoded) != 0) return 2;
    (void)snprintf(local_uri, sizeof local_uri, "ldapi://%s", encoded);
    (void)snprintf(tcp_uri, sizeof tcp_uri, "ldap://127.0.0.1:%ld/", port);
    (void)snprintf(listeners, sizeof listeners, "%s %s", tcp_uri, local_uri);
    char *server_args[] = { "/usr/sbin/slapd", "-f", config, "-h", listeners, "-d", "0", NULL };
    char *seed_args[] = { "/usr/sbin/slapadd", "-f", config, "-l", input, NULL };
    char *modify_args[] = { "/usr/bin/ldapmodify", "-Q", "-Y", "EXTERNAL", "-H", local_uri, "-f", input, NULL };
    char *oracle_args[] = { "/usr/bin/ldapsearch", "-LLL", "-o", "ldif-wrap=no", "-x", "-H", tcp_uri,
        "-l", "1", "-z", "256", "-b", "ou=groups,dc=example,dc=test", "(objectClass=groupOfNames)", "cn", "member", "entryUUID", NULL };
    char **command = server ? server_args : seed ? seed_args : modify ? modify_args : oracle_args;
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_DFL;
    sigemptyset(&action.sa_mask);
    if (sigaction(SIGCHLD, &action, NULL) != 0) return 3;
    action.sa_handler = SIG_IGN;
    if (sigaction(SIGPIPE, &action, NULL) != 0) return 3;
    int out[2], err[2];
    if (pipe2(out, O_CLOEXEC) != 0 || pipe2(err, O_CLOEXEC) != 0) return 3;
    pid_t owner = getpid();
    pid_t child = fork();
    if (child < 0) return 3;
    if (child == 0) {
        if (prctl(PR_SET_PDEATHSIG, SIGKILL) != 0 || getppid() != owner) _exit(93);
        action.sa_handler = SIG_DFL;
        (void)sigaction(SIGPIPE, &action, NULL);
        int input_fd = open("/dev/null", O_RDONLY);
        if (input_fd < 0 || dup2(input_fd, STDIN_FILENO) < 0 || dup2(out[1], STDOUT_FILENO) < 0 || dup2(err[1], STDERR_FILENO) < 0) _exit(94);
        close(input_fd); close(out[0]); close(out[1]); close(err[0]); close(err[1]);
        if (clearenv() != 0 || setenv("LDAPNOINIT", "1", 1) != 0 || setenv("HOME", argv[2], 1) != 0 ||
            setenv("LDAPCONF", ldapconf, 1) != 0 || setenv("LANG", "C.UTF-8", 1) != 0) _exit(95);
        execv(command[0], command);
        _exit(96);
    }
    close(out[1]); close(err[1]);
    nonblocking(out[0]); nonblocking(err[0]); nonblocking(STDIN_FILENO);
    char *output = malloc(OUTPUT_CAP);
    if (output == NULL) {
        (void)kill(child, SIGKILL);
        while (waitpid(child, NULL, 0) < 0 && errno == EINTR) { }
        return 3;
    }
    size_t used = 0, stdout_seen = 0, stderr_seen = 0;
    long long start = milliseconds(), termination = 0;
    int stopping = 0, killed = 0, failure = 0, requested = 0;
    for (;;) {
        if (exited(child)) break;
        if (consume(out[0], output, &used, oracle, &stdout_seen, oracle ? OUTPUT_CAP : 65536U) != 0 ||
            consume(err[0], output, &used, 0, &stderr_seen, 65536U) != 0) failure = 1;
        char control[32];
        ssize_t n = read(STDIN_FILENO, control, sizeof control);
        if (n >= 0) requested = 1;
        else if (errno != EAGAIN && errno != EWOULDBLOCK && errno != EINTR) requested = 1;
        if (!server && milliseconds() - start >= 5000) failure = 1;
        if (!stopping && (requested || failure)) {
            (void)kill(child, SIGTERM);
            termination = milliseconds();
            stopping = 1;
        }
        if (stopping && !killed && milliseconds() - termination >= 250) {
            (void)kill(child, SIGKILL);
            killed = 1;
        }
        struct pollfd descriptors[3] = { { STDIN_FILENO, POLLIN | POLLHUP, 0 }, { out[0], POLLIN, 0 }, { err[0], POLLIN, 0 } };
        (void)poll(descriptors, 3, 10);
    }
    /* No signal is sent after this reap. The PID remained owned, even as a zombie. */
    int status = 0;
    while (waitpid(child, &status, 0) < 0) { if (errno != EINTR) { failure = 1; break; } }
    if (consume(out[0], output, &used, oracle, &stdout_seen, oracle ? OUTPUT_CAP : 65536U) != 0 ||
        consume(err[0], output, &used, 0, &stderr_seen, 65536U) != 0) failure = 1;
    close(out[0]); close(err[0]);
    int result = failure ? 20 : requested && server ? 0 : WIFEXITED(status) && WEXITSTATUS(status) == 0 ? 0 : 20;
    if (result == 0 && oracle && emit(output, used) != 0) result = 20;
    free(output);
    if (result != 0) {
        const char message[] = "OWNER_FAILURE\n";
        ssize_t written = write(STDERR_FILENO, message, sizeof message - 1);
        if (written != (ssize_t)(sizeof message - 1)) return 21;
    }
    return result;
}
