all: libtextoolwrap.so

clean:
	rm -f textoolwrap.o
	rm -f libtextoolwrap.so

textoolwrap.o: textoolwrap.cpp
	$(CXX) -c -fpic -o textoolwrap.o textoolwrap.cpp

libtextoolwrap.so: textoolwrap.o
	$(CXX) -shared -o libtextoolwrap.so textoolwrap.o -Lcrunch/linux64 -lcrnlib -Wl,-rpath,"\$$ORIGIN"