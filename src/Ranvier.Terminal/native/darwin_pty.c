#include <errno.h>
#include <fcntl.h>
#include <pthread.h>
#include <signal.h>
#include <sys/ioctl.h>
#include <sys/wait.h>
#include <unistd.h>

// The managed parent retains the nonblocking master and the wait/reap policy.
// Only this native child runs between fork and exec.
int ranvier_spawn_pty(int *pid, int master, int slave, const char *directory,
                      const char *executable, char *const argv[], char *const envp[]) {
    int channel[2];
    if (pipe(channel) < 0) return errno;
    int writer = fcntl(channel[1], F_DUPFD_CLOEXEC, 3);
    int error = writer < 0 ? errno : 0;
    close(channel[1]);
    if (!error && fcntl(channel[0], F_SETFD, FD_CLOEXEC) < 0) error = errno;

    sigset_t all, previous, empty;
    sigfillset(&all);
    sigemptyset(&empty);
    if (!error) error = pthread_sigmask(SIG_SETMASK, &all, &previous);
    if (error) {
        close(channel[0]);
        if (writer >= 0) close(writer);
        return error;
    }

    pid_t child = fork();
    if (child == 0) {
        close(channel[0]);
        close(master);
        if (setsid() == -1 || ioctl(slave, TIOCSCTTY, 0) == -1 || chdir(directory) == -1)
            goto failed;
        for (int fd = 0; fd < 3; fd++) {
            if (dup2(slave, fd) == -1 || fcntl(fd, F_SETFD, 0) == -1) goto failed;
        }
        if (slave > 2) close(slave);
        struct sigaction normal = { .sa_handler = SIG_DFL };
        sigemptyset(&normal.sa_mask);
        for (int sig = 1; sig < NSIG; sig++) {
            if (sig != SIGKILL && sig != SIGSTOP && sigaction(sig, &normal, NULL) == -1)
                goto failed;
        }
        if (sigprocmask(SIG_SETMASK, &empty, NULL) == -1) goto failed;
        execve(executable, argv, envp);
    failed:
        error = errno;
        while (write(writer, &error, sizeof(error)) == -1 && errno == EINTR) {}
        _exit(127);
    }

    error = child < 0 ? errno : 0;
    pthread_sigmask(SIG_SETMASK, &previous, NULL);
    close(writer);
    if (child > 0) {
        int failure = 0;
        ssize_t count;
        do { count = read(channel[0], &failure, sizeof(failure)); } while (count < 0 && errno == EINTR);
        if (count != 0) {
            error = count < 0 ? errno : count == sizeof(failure) ? failure : EIO;
            if (count < 0) kill(child, SIGKILL);
            while (waitpid(child, NULL, 0) < 0 && errno == EINTR) {}
        } else {
            *pid = child;
        }
    }
    close(channel[0]);
    return error;
}
